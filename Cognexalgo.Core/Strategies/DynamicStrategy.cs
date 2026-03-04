using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cognexalgo.Core.Models;
using Cognexalgo.Core.Rules;
using Cognexalgo.Core.Services;
using Newtonsoft.Json;

namespace Cognexalgo.Core.Strategies
{
    public class DynamicStrategy : StrategyBase
    {
        private readonly DynamicStrategyConfig _config;
        private readonly RuleEvaluator _evaluator;
        private readonly EvaluationContext _context;
        private readonly RiskManager _riskManager;
        private readonly CandleAggregator _aggregator;
        private readonly List<Skender.Stock.Indicators.Quote> _history = new List<Skender.Stock.Indicators.Quote>();

        // One-shot guard: once we enter and then exit, never re-enter in the same session.
        // Prevents continuous entry signals when entry conditions remain true after exit.
        private bool _hasEnteredOnce = false;

        public DynamicStrategy(TradingEngine engine, string jsonConfig) 
            : base(engine, "Dynamic")
        {
            try 
            {
                // [FIX] Handle cases where the JSON might be double-stringified or malformed
                _config = JsonConvert.DeserializeObject<DynamicStrategyConfig>(jsonConfig);
                
                if (_config != null)
                {
                    Name = _config.StrategyName ?? "Unnamed_Dynamic";
                    engine.Logger?.Log("Strategy", $"Loaded Dynamic Strategy: {Name} for {_config.Symbol} with {_config.EntryRules.Count} Entry Rules");
                    
                    // [NEW] Resilient Rules Deserialization 
                    HandleStringifiedRules();
                    
                    if (string.IsNullOrEmpty(_config.StrategyName))
                    {
                        // Generate a fallback name only if it's truly missing
                        Name = $"Strategy_{_config.Symbol}_{DateTime.Now.Ticks}";
                        engine.Logger?.Log("Strategy", $"[WARNING] StrategyName missing in config. Assigned fallback: {Name}");
                    }
                    else
                    {
                        Name = _config.StrategyName;
                    }
                }
                else 
                {
                    engine.Logger?.Log("Strategy", "ERROR: Deserialized DynamicStrategyConfig is NULL");
                }
            }
            catch (Exception ex)
            {
                engine.Logger?.Log("Strategy", $"CRITICAL: Failed to deserialize DynamicStrategyConfig: {ex.Message} | JSON: {jsonConfig}");
            }
            
            _evaluator = new RuleEvaluator();
            _context = new EvaluationContext(new List<Skender.Stock.Indicators.Quote>());

            if (_config != null)
            {
                _riskManager = new RiskManager(engine, _config.StrategyName, _config.Symbol, _config.TotalLots, _config.ExitSettings, _config.ExitRules);
                
                // Initialize Aggregator based on timeframe
                int timeframe = ParseTimeframe(_config.Timeframe);
                _aggregator = new CandleAggregator(timeframe);
                _aggregator.OnCandleClosed += (candle) => _ = OnCandleClosedAsync(candle);
            }
            
            _context = new EvaluationContext(_history);
        }

        private void HandleStringifiedRules()
        {
            // The ResilientRuleListConverter handles the property-level deserialization 
            // the constructor just ensure the Name is set and other basics.
            // But if we ever need to manually fix them, we can do it here.
            if (_config.EntryRules == null) _config.EntryRules = new List<Rule>();
            if (_config.ExitRules == null) _config.ExitRules = new List<Rule>();
        }

        public async Task InitializeAsync(List<Skender.Stock.Indicators.Quote> history)
        {
            if (history != null && history.Any())
            {
                _history.Clear();
                _history.AddRange(history);
                Console.WriteLine($"[Strategy] {Name} initialized with {_history.Count} historical candles.");
            }
            await Task.CompletedTask;
        }

        private int ParseTimeframe(string timeframe)
        {
            if (string.IsNullOrEmpty(timeframe)) return 1;
            if (timeframe.Contains("5")) return 5;
            if (timeframe.Contains("15")) return 15;
            if (timeframe.Contains("30")) return 30;
            if (timeframe.Contains("60")) return 60;
            return 1; // Default 1 min
        }

        public override async Task OnTickAsync(TickerData ticker)
        {
            if (!IsActive || _config == null) return;

            double ltp = GetLtp(ticker, _config.Symbol);
            if (ltp <= 0) return;

            // 1. Feed Aggregator (Forms 1-min, 5-min candles etc.)
            _aggregator.AddTick(DateTime.Now, (decimal)ltp);

            // 2. Evaluate Exits on EVERY TICK (Price-based SL/Target)
            if (_riskManager.IsPositionOpen)
            {
                await _riskManager.CheckExits(ltp, _context);
            }
            else if (!_hasEnteredOnce)
            {
                // 3. Evaluate Entries on EVERY TICK (only before first entry)
                // We construct a temporary context that includes the historically closed
                // candles PLUS the currently forming synthetic candle at this exact tick.
                var syntheticCandle = new Skender.Stock.Indicators.Quote 
                { 
                    Date = DateTime.Now, 
                    Open = (decimal)ltp, 
                    High = (decimal)ltp, 
                    Low = (decimal)ltp, 
                    Close = (decimal)ltp, 
                    Volume = 1 
                };

                var liveHistory = new List<Skender.Stock.Indicators.Quote>(_history);
                liveHistory.Add(syntheticCandle);

                var liveContext = new EvaluationContext(liveHistory);
                await EvaluateEntryRulesAsync(liveContext, ltp);
            }
        }

        private async Task EvaluateEntryRulesAsync(EvaluationContext context, double currentPrice)
        {
            if (_config.EntryRules == null || !_config.EntryRules.Any()) 
            {
                // _engine.Logger?.Log("Strategy", $"[{Name}] No entry rules defined.");
                return;
            }

            foreach (var rule in _config.EntryRules)
            {
                bool isMatch = _evaluator.Evaluate(rule, context, Name, (msg) => _engine.Logger?.Log("Strategy", msg));
                // _engine.Logger?.Log("Strategy", $"[RuleEval] {Name} => Rule {rule.Action}: Match={isMatch}");

                if (isMatch)
                {
                    string actionMsg = $"[Dynamic] {Name} Live Entry Signal: {rule.Action} @ {currentPrice}";
                    _engine.Logger?.Log("Strategy", actionMsg);
                    Console.WriteLine(actionMsg);
                    
                    BroadcastSignal(new Signal
                    {
                        Timestamp = DateTime.Now,
                        StrategyName = Name,
                        Symbol = _config.Symbol,
                        SignalType = rule.Action,
                        Price = currentPrice,
                        Reason = rule.Action
                    });

                    await _engine.ExecuteOrderAsync(new StrategyConfig { Id=0, Name=Name }, _config.Symbol, rule.Action);
                    _riskManager.InitializeEntry(currentPrice, context);
                    _hasEnteredOnce = true; // One-shot: never re-enter after first entry
                    break; // Only take one entry per tick
                }
            }
        }

        private async Task OnCandleClosedAsync(Skender.Stock.Indicators.Quote candle)
        {
            if (!IsActive || _config == null) return;

            // 1. Add closed candle to history for Indicator calculation
            _history.Add(candle);
            
            // Limit history size to 500 candles to save memory
            if (_history.Count > 500) _history.RemoveAt(0);

            _engine.Logger?.Log("Strategy", $"[{Name}] Candle Closed: {candle.Date} | Close: {candle.Close}");

            // 2. Evaluate Entry Rules on CANDLE CLOSE (Fallback/Standard)
            // If the position wasn't already opened during the live ticks of this candle,
            // check again on the confirmed close.
            if (!_riskManager.IsPositionOpen)
            {
                 // We can reuse the same tick evaluation logic with the standard context 
                 // since _history now includes the recently closed candle.
                 await EvaluateEntryRulesAsync(_context, (double)candle.Close);
            }
        }
        
        private double GetLtp(TickerData ticker, string symbol)
        {
            if (symbol == "NIFTY" && ticker.Nifty != null) return ticker.Nifty.Ltp;
            if (symbol == "BANKNIFTY" && ticker.BankNifty != null) return ticker.BankNifty.Ltp;
            // Add Options lookup if needed
            return 0;
        }
    }
}
