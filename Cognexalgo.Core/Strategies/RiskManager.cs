using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cognexalgo.Core.Models;
using Cognexalgo.Core.Rules;
using Newtonsoft.Json;

namespace Cognexalgo.Core.Strategies
{
    public class RiskManager
    {
        private readonly TradingEngine _engine;
        private readonly string _strategyName;
        private readonly string _symbol;
        private readonly int _totalLots;
        private readonly ExitConfig _exitSettings;
        private readonly List<Rule> _exitRules;
        private readonly RuleEvaluator _evaluator;

        public bool IsPositionOpen { get; private set; } = false;
        public string EnteredSymbol { get; private set; } = "";
        public double EntryPrice { get; private set; } = 0;
        public double TargetPrice { get; private set; } = 0;
        public double SlPrice { get; private set; } = 0;
        public double HighestPrice { get; private set; } = 0;
        private bool _breakevenTriggered = false;

        public RiskManager(TradingEngine engine, string strategyName, string symbol, int totalLots, ExitConfig exitSettings, List<Rule> exitRules = null)
        {
            _engine = engine;
            _strategyName = strategyName;
            _symbol = symbol;
            _totalLots = totalLots;
            _exitSettings = exitSettings;
            _exitRules = exitRules ?? new List<Rule>();
            _evaluator = new RuleEvaluator();
        }

        public void InitializeEntry(double entryPrice, EvaluationContext context, string enteredSymbol = "")
        {
            IsPositionOpen = true;
            EnteredSymbol = enteredSymbol;
            EntryPrice = entryPrice;
            HighestPrice = entryPrice;
            _breakevenTriggered = false;

            if (_exitSettings?.PartialExitLevels != null)
            {
                foreach (var level in _exitSettings.PartialExitLevels)
                    level.IsExited = false;
            }

            CalculateExitLevels(entryPrice, context);
        }

        public void Reset()
        {
            IsPositionOpen = false;
            EnteredSymbol = "";
            EntryPrice = 0;
            HighestPrice = 0;
            _breakevenTriggered = false;
        }

        private void CalculateExitLevels(double entryPrice, EvaluationContext context)
        {
            if (_exitSettings == null) return;

            // TARGET CALCULATION
            if (_exitSettings.TargetType == TargetType.Percentage)
            {
                TargetPrice = entryPrice * (1 + (_exitSettings.TargetValue / 100));
            }
            else if (_exitSettings.TargetType == TargetType.AbsolutePoints)
            {
                TargetPrice = entryPrice + _exitSettings.TargetValue;
            }
            else if (_exitSettings.TargetType == TargetType.ATR)
            {
                double atr = context.GetIndicatorValue(IndicatorType.ATR, _exitSettings.AtrPeriod);
                if (atr <= 0) atr = entryPrice * 0.01; 
                TargetPrice = entryPrice + (atr * _exitSettings.AtrMultiplier);
            }

            // STOPLOSS CALCULATION
            if (_exitSettings.StopLossType == StopLossType.Percentage)
            {
                SlPrice = entryPrice * (1 - (_exitSettings.StopLossValue / 100));
            }
            else if (_exitSettings.StopLossType == StopLossType.AbsolutePoints)
            {
                SlPrice = entryPrice - _exitSettings.StopLossValue;
            }
            else if (_exitSettings.StopLossType == StopLossType.ATR)
            {
                double atr = context.GetIndicatorValue(IndicatorType.ATR, _exitSettings.AtrPeriod);
                if (atr <= 0) atr = entryPrice * 0.01;
                SlPrice = entryPrice - (atr * _exitSettings.AtrMultiplier);
            }
            
            Console.WriteLine($"[RiskManager] Exit Levels Set: Target={TargetPrice:F2}, SL={SlPrice:F2}");
        }

        public async Task<bool> CheckExits(double ltp, EvaluationContext context)
        {
            if (!IsPositionOpen || _exitSettings == null) return false;

            double profitPercent = ((ltp - EntryPrice) / EntryPrice) * 100;

            // 1. Trailing Stop Loss
            if (_exitSettings.TrailingStopLoss && ltp > HighestPrice)
            {
                HighestPrice = ltp;
                if (_exitSettings.TrailingStopIsPercent)
                    SlPrice = HighestPrice * (1 - (_exitSettings.TrailingStopDistance / 100));
                else
                    SlPrice = HighestPrice - _exitSettings.TrailingStopDistance;
            }

            // 2. Time-Based Exit
            if (_exitSettings.EnableTimeBasedExit)
            {
                if (TimeSpan.TryParse(_exitSettings.ExitTime, out var exitTime))
                {
                    if (DateTime.Now.TimeOfDay >= exitTime)
                    {
                        await ExecuteFullExit(ltp, "Time-Based Exit");
                        return true;
                    }
                }
            }

            // 3. Breakeven Stop
            if (_exitSettings.EnableBreakevenStop && !_breakevenTriggered)
            {
                if (profitPercent >= _exitSettings.BreakevenTriggerPercent)
                {
                    SlPrice = EntryPrice;
                    _breakevenTriggered = true;
                }
            }

            // 4. Partial Exits
            if (_exitSettings.EnablePartialExits)
            {
                foreach (var level in _exitSettings.PartialExitLevels)
                {
                    if (!level.IsExited && profitPercent >= level.TargetPercent)
                    {
                        var (_, lotSize) = _engine.TokenService.GetInstrumentInfo(_symbol);
                        if (lotSize <= 0) lotSize = 1;

                        int qtyToExit = (int)((_totalLots * lotSize) * (level.Percentage / 100));
                        if (qtyToExit <= 0) qtyToExit = 1;

                        await _engine.ExecuteOrderAsync(CreateDummyConfig(), _symbol, "EXIT", qtyToExit);
                        level.IsExited = true;
                        Console.WriteLine($"[RiskManager] Partial Exit: {level.Percentage}% @ {ltp}");
                    }
                }
            }

            // 5. Profit Protection
            if (_exitSettings.EnableProfitProtection)
            {
                if (profitPercent >= _exitSettings.ProfitProtectionTrigger)
                {
                    double unrealizedProfit = ltp - EntryPrice;
                    double protectedSl = EntryPrice + (unrealizedProfit * (_exitSettings.ProfitProtectionPercent / 100));
                    if (protectedSl > SlPrice) SlPrice = protectedSl;
                }
            }

            // 6. Standard Target
            if (TargetPrice > 0 && ltp >= TargetPrice)
            {
                await ExecuteFullExit(ltp, "Target Hit");
                return true;
            }

            // 7. Standard Stop Loss
            if (SlPrice > 0 && ltp <= SlPrice)
            {
                await ExecuteFullExit(ltp, "StopLoss Hit");
                return true;
            }

            // 8. Signal-Based Exits
            foreach (var rule in _exitRules)
            {
                if (_evaluator.Evaluate(rule, context))
                {
                    await ExecuteFullExit(ltp, "Signal Triggered");
                    return true;
                }
            }

            return false;
        }

        private async Task ExecuteFullExit(double ltp, string reason)
        {
            Console.WriteLine($"[RiskManager] Full Exit: {reason} @ {ltp}");

            // Record performance for the engine to broadcast
            double potentialProfit = HighestPrice - EntryPrice;
            double protectedProfit = SlPrice - EntryPrice;

            // Use the actual option symbol entered (not the underlying index)
            string exitSymbol = !string.IsNullOrEmpty(EnteredSymbol) ? EnteredSymbol : _symbol;
            await _engine.ExecuteOrderAsync(CreateDummyConfig(), exitSymbol, "EXIT", 0, ltp, potentialProfit, protectedProfit);
            IsPositionOpen = false;
            EnteredSymbol = "";
        }

        private StrategyConfig CreateDummyConfig()
        {
            // We need to pass the config back to ExecuteOrderAsync so it knows the TotalLots etc if needed
            // But usually the StrategyBase.Name is used. 
            // Minimal config for TradingEngine to work.
            return new StrategyConfig 
            { 
                Name = _strategyName, 
                StrategyType = "CUSTOM", 
                Parameters = JsonConvert.SerializeObject(new { TotalLots = _totalLots }) 
            };
        }
    }
}
