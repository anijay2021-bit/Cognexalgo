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

        public DynamicStrategy(TradingEngine engine, string jsonConfig) 
            : base(engine, "Dynamic")
        {
            _config = JsonConvert.DeserializeObject<DynamicStrategyConfig>(jsonConfig);
            if (_config != null)
            {
                Name = _config.StrategyName;
            }
            
            _evaluator = new RuleEvaluator();
            _context = new EvaluationContext(new System.Collections.Generic.List<Skender.Stock.Indicators.Quote>());

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

        public async Task InitializeAsync(List<Skender.Stock.Indicators.Quote> history)
        {
            if (history != null && history.Any())
            {
                _history.AddRange(history);
                Console.WriteLine($"[Strategy] {Name} initialized with {history.Count} historical candles.");
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
        }

        private async Task OnCandleClosedAsync(Skender.Stock.Indicators.Quote candle)
        {
            if (!IsActive || _config == null) return;

            // 1. Add closed candle to history for Indicator calculation
            _history.Add(candle);
            
            // Limit history size to 500 candles to save memory
            if (_history.Count > 500) _history.RemoveAt(0);

            _engine.Logger?.Log("Strategy", $"[{Name}] Candle Closed: {candle.Date} | Close: {candle.Close}");

            // 2. Evaluate Entry Rules ONLY ON CANDLE CLOSE
            if (!_riskManager.IsPositionOpen)
            {
                foreach (var rule in _config.EntryRules)
                {
                    if (_evaluator.Evaluate(rule, _context))
                    {
                        Console.WriteLine($"[Dynamic] {_config.StrategyName} Entry: {rule.Action} @ {candle.Close}");
                        
                        BroadcastSignal(new Signal 
                        {
                            Timestamp = DateTime.Now,
                            StrategyName = _config.StrategyName,
                            Symbol = _config.Symbol,
                            SignalType = rule.Action,
                            Price = (double)candle.Close,
                            Reason = rule.Action
                        });

                        await _engine.ExecuteOrderAsync(new StrategyConfig { Id=0, Name=_config.StrategyName }, _config.Symbol, rule.Action);
                        _riskManager.InitializeEntry((double)candle.Close, _context);
                        break;
                    }
                }
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
