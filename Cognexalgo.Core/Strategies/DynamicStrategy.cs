using System;
using System.Threading.Tasks;
using Cognexalgo.Core.Models;
using Cognexalgo.Core.Rules;
using Newtonsoft.Json;

namespace Cognexalgo.Core.Strategies
{
    public class DynamicStrategy : StrategyBase
    {
        private readonly DynamicStrategyConfig _config;
        private readonly RuleEvaluator _evaluator;
        private readonly EvaluationContext _context;
        private readonly RiskManager _riskManager;

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
            }
        }

        public override async Task OnTickAsync(TickerData ticker)
        {
            if (!IsActive || _config == null) return;

            double ltp = GetLtp(ticker, _config.Symbol);
            if (ltp <= 0) return;

            // 1. Update Context (Real-time logic)
            // _context.LTP = ltp; // Read-only
            _context.AddCandidate(new Skender.Stock.Indicators.Quote { Date = DateTime.Now, Close = (decimal)ltp });

            // In a real implementation:
            // var quotes = _historyService.GetQuotes(_config.Symbol, _config.Timeframe);
            
            // Example of how they would be calculated and pushed to Context:
            // _context.SetIndicator(IndicatorType.RSI, 14, quotes.GetRsi(14).Last().Rsi);
            // _context.SetIndicator(IndicatorType.ATR, 14, quotes.GetAtr(14).Last().Atr);
            // _context.SetIndicator(IndicatorType.VWAP, 0, quotes.GetVwap().Last().Vwap);
            
            // Patterns (1.0 = Found, 0.0 = Not Found)
            // _context.SetIndicator(IndicatorType.PATTERN_DOJI, 0, quotes.GetDoji().Last().Match != Match.None ? 1 : 0);
            // _context.SetIndicator(IndicatorType.PATTERN_MARUBOZU, 0, quotes.GetMarubozu().Last().Match != Match.None ? 1 : 0);

            // 2. Evaluate Entry Rules (If No Position)
            if (!_riskManager.IsPositionOpen)
            {
                foreach (var rule in _config.EntryRules)
                {
                    if (_evaluator.Evaluate(rule, _context))
                    {
                        // Execute Action
                        Console.WriteLine($"[Dynamic] {_config.StrategyName} Entry: {rule.Action} @ {ltp}");
                        
                        BroadcastSignal(new Signal 
                        {
                            Timestamp = DateTime.Now,
                            StrategyName = _config.StrategyName,
                            Symbol = _config.Symbol,
                            SignalType = rule.Action,
                            Price = ltp,
                            Reason = rule.Action
                        });

                        // Execute Order
                        await _engine.ExecuteOrderAsync(new StrategyConfig { Id=0, Name=_config.StrategyName }, _config.Symbol, rule.Action);

                        _riskManager.InitializeEntry(ltp, _context);
                        break; // Trigger first matching rule
                    }
                }
            }
            else
            {
                // 3. Evaluate Risk/Exit Logic
                await _riskManager.CheckExits(ltp, _context);
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
