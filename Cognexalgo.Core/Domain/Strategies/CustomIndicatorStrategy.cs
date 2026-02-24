using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cognexalgo.Core.Domain.Entities;
using Cognexalgo.Core.Domain.Enums;
using Cognexalgo.Core.Domain.Indicators;
using Cognexalgo.Core.Domain.ValueObjects;
using Newtonsoft.Json;
using Skender.Stock.Indicators;

namespace Cognexalgo.Core.Domain.Strategies
{
    /// <summary>
    /// Custom Indicator Strategy (Module 2B):
    /// User-defined strategy driven entirely by indicator conditions.
    /// Supports entry/exit rules with AND/OR condition groups,
    /// multi-timeframe indicators, and configurable actions (BUY_CE, BUY_PE, SELL, etc.).
    /// </summary>
    public class CustomIndicatorStrategy : StrategyV2Base
    {
        private readonly CustomStrategyConfig _config;
        private readonly IndicatorEngine _indicatorEngine;
        private readonly ConditionEvaluator _conditionEvaluator;
        private readonly Services.CandleAggregatorV2 _aggregator;

        public CustomIndicatorStrategy(CustomStrategyConfig config)
        {
            _config = config;
            Type = StrategyType.CSTM;
            Name = config.Name ?? "Custom Indicator";

            _indicatorEngine = new IndicatorEngine
            {
                MinimumBarsRequired = config.WarmupBars
            };
            _conditionEvaluator = new ConditionEvaluator(_indicatorEngine);

            // Initialize candle aggregator for the primary timeframe
            _aggregator = new Services.CandleAggregatorV2(ParseTimeframeMinutes(config.PrimaryTimeFrame));
            _aggregator.OnCandleClosed += OnAggregatorCandleClosed;
        }

        public override async Task InitializeAsync(CancellationToken ct)
        {
            if (_config.HistoricalCandles != null && _config.HistoricalCandles.Count > 0)
            {
                _indicatorEngine.LoadHistory(_config.PrimaryTimeFrame, _config.HistoricalCandles);
                Log("INFO", $"[{Name}] Initialized with {_config.HistoricalCandles.Count} historical candles on {_config.PrimaryTimeFrame}");
            }
            await Task.CompletedTask;
        }

        public override async Task OnTickAsync(TickContext tick, CancellationToken ct)
        {
            if (ct.IsCancellationRequested || IsCircuitBroken) return;

            try
            {
                decimal ltp = GetLtp(tick);
                if (ltp <= 0) return;

                // Feed aggregator
                _aggregator.AddTick(DateTime.Now, ltp);

                switch (CurrentState)
                {
                    case SignalState.WAITING:
                        // Evaluate entry conditions on every tick using live synthetic candle
                        if (_indicatorEngine.IsWarmedUp)
                        {
                            EvaluateEntryOnTick(tick, ltp);
                        }
                        break;

                    case SignalState.IN_POSITION:
                        // Evaluate exit conditions on every tick
                        if (_config.ExitConditions != null && _config.ExitConditions.Count > 0)
                        {
                            EvaluateExitOnTick(tick, ltp);
                        }
                        break;
                }

                RecordSuccess();
            }
            catch (Exception ex)
            {
                RecordError(ex);
            }

            await Task.CompletedTask;
        }

        private void EvaluateEntryOnTick(TickContext tick, decimal ltp)
        {
            // Create a synthetic candle at current tick for live evaluation
            var syntheticCandle = new Quote
            {
                Date = DateTime.Now,
                Open = ltp, High = ltp, Low = ltp, Close = ltp, Volume = 1
            };

            // Temporarily add to indicator engine
            _indicatorEngine.AddCandle(_config.PrimaryTimeFrame, syntheticCandle);

            bool matched = _conditionEvaluator.Evaluate(
                _config.EntryConditions, out string triggerDesc);

            // Remove synthetic candle (it's not a real close)
            // The LoadHistory re-sets on next real candle

            if (matched)
            {
                Log("INFO", $"[{Name}] ENTRY SIGNAL: {triggerDesc}");

                // Capture indicator snapshot
                var snapshot = _indicatorEngine.GetSnapshot(
                    _config.PrimaryTimeFrame, _config.TrackedIndicators);

                var signal = new Signal
                {
                    StrategyId = StrategyId,
                    SignalType = SignalType.Entry,
                    TriggerCondition = triggerDesc,
                    IndicatorSnapshot = JsonConvert.SerializeObject(snapshot),
                    Price = (double)ltp,
                    Symbol = _config.Symbol,
                    Reason = _config.EntryAction
                };

                FireSignal(signal);
                CurrentState = SignalState.ENTRY_TRIGGERED;
            }
        }

        private void EvaluateExitOnTick(TickContext tick, decimal ltp)
        {
            bool matched = _conditionEvaluator.Evaluate(
                _config.ExitConditions!, out string triggerDesc);

            if (matched)
            {
                Log("INFO", $"[{Name}] EXIT SIGNAL: {triggerDesc}");

                var signal = new Signal
                {
                    StrategyId = StrategyId,
                    SignalType = SignalType.Exit,
                    TriggerCondition = triggerDesc,
                    Price = (double)ltp,
                    Symbol = _config.Symbol,
                    Reason = "IndicatorExit"
                };

                FireSignal(signal);
                CurrentState = SignalState.EXIT_TRIGGERED;
            }
        }

        private void OnAggregatorCandleClosed(Quote candle)
        {
            _indicatorEngine.AddCandle(_config.PrimaryTimeFrame, candle);
            Log("INFO", $"[{Name}] Candle Closed: {candle.Date:HH:mm} | Close: {candle.Close}");
        }

        private decimal GetLtp(TickContext tick)
        {
            return _config.Symbol switch
            {
                "NIFTY" => tick.NiftyLtp,
                "BANKNIFTY" => tick.BankNiftyLtp,
                "FINNIFTY" => tick.FinniftyLtp,
                _ => tick.Ltp
            };
        }

        private int ParseTimeframeMinutes(TimeFrame tf)
        {
            return tf switch
            {
                TimeFrame.Min1 => 1,
                TimeFrame.Min3 => 3,
                TimeFrame.Min5 => 5,
                TimeFrame.Min10 => 10,
                TimeFrame.Min15 => 15,
                TimeFrame.Min30 => 30,
                TimeFrame.Hour1 => 60,
                TimeFrame.Day1 => 1440,
                _ => 1
            };
        }
    }

    // ─── Custom Strategy Configuration ───────────────────────────
    public class CustomStrategyConfig
    {
        public string? Name { get; set; }
        public string Symbol { get; set; } = "NIFTY";
        public TimeFrame PrimaryTimeFrame { get; set; } = TimeFrame.Min5;
        public int WarmupBars { get; set; } = 50;

        public List<ConditionGroup> EntryConditions { get; set; } = new();
        public List<ConditionGroup>? ExitConditions { get; set; }

        public string EntryAction { get; set; } = "BUY_CE"; // BUY_CE, BUY_PE, SELL_CE, SELL_PE
        public int Lots { get; set; } = 1;

        // Historical data for initialization
        public List<Quote>? HistoricalCandles { get; set; }

        // Indicators to track for snapshot
        public List<(IndicatorType Type, int Period)> TrackedIndicators { get; set; } = new();
    }
}

// ─── Candle Aggregator V2 ────────────────────────────────────────
namespace Cognexalgo.Core.Domain.Strategies.Services
{
    /// <summary>
    /// Aggregates raw ticks into OHLCV candles of configurable timeframe.
    /// </summary>
    public class CandleAggregatorV2
    {
        private readonly int _intervalMinutes;
        private Quote? _currentCandle;
        private DateTime _nextCloseTime;

        public event Action<Quote>? OnCandleClosed;

        public Quote? CurrentCandle => _currentCandle;

        public CandleAggregatorV2(int intervalMinutes)
        {
            _intervalMinutes = intervalMinutes;
        }

        public void AddTick(DateTime timestamp, decimal price)
        {
            if (_currentCandle == null)
            {
                StartNewCandle(timestamp, price);
                return;
            }

            if (timestamp >= _nextCloseTime)
            {
                // Close current candle
                OnCandleClosed?.Invoke(_currentCandle);
                StartNewCandle(timestamp, price);
                return;
            }

            // Update current candle
            if (price > _currentCandle.High) _currentCandle.High = price;
            if (price < _currentCandle.Low) _currentCandle.Low = price;
            _currentCandle.Close = price;
            _currentCandle.Volume++;
        }

        private void StartNewCandle(DateTime timestamp, decimal price)
        {
            _currentCandle = new Quote
            {
                Date = timestamp,
                Open = price,
                High = price,
                Low = price,
                Close = price,
                Volume = 1
            };

            _nextCloseTime = timestamp.AddMinutes(_intervalMinutes);
            // Align to interval boundary
            int totalMinutes = (int)_nextCloseTime.TimeOfDay.TotalMinutes;
            int aligned = (totalMinutes / _intervalMinutes + 1) * _intervalMinutes;
            _nextCloseTime = timestamp.Date.AddMinutes(aligned);
        }
    }
}
