using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cognexalgo.Core.Models;
using Cognexalgo.Core.Rules;
using Skender.Stock.Indicators;

namespace Cognexalgo.Core.Strategies
{
    public class FourEmaStrategy : StrategyBase
    {
        private List<Quote> _history = new List<Quote>();
        
        private readonly RiskManager _riskManager;
        private readonly EvaluationContext _context;

        // Settings
        public string Symbol { get; set; } = "NIFTY"; // Default
        public int TimeFrameMinutes { get; set; } = 5;

        public FourEmaStrategy(TradingEngine engine) : base(engine, "4 EMA Strategy") 
        { 
            _context = new EvaluationContext(_history);
            // Default Exit Settings for 4 EMA Strategy
            var exitSettings = new ExitConfig
            {
                TargetType = TargetType.Percentage,
                TargetValue = 2.0,
                StopLossType = StopLossType.Percentage,
                StopLossValue = 1.0,
                TrailingStopLoss = true,
                TrailingStopDistance = 0.5,
                TrailingStopIsPercent = true
            };
            _riskManager = new RiskManager(engine, "4 EMA Strategy", Symbol, 1, exitSettings);
        }

        public override async Task OnTickAsync(TickerData ticker)
        {
            if (!IsActive) return;

            // Fetch LTP from Ticker Data
            double ltp = 0; 
            if (Symbol == "NIFTY") ltp = ticker.Nifty.Ltp;
            else if (Symbol == "BANKNIFTY") ltp = ticker.BankNifty.Ltp;
            if (ltp <= 0) return;

            // Update context candidate
            _context.AddCandidate(new Quote { Date = DateTime.Now, Close = (decimal)ltp });

            if (_riskManager.IsPositionOpen)
            {
                await _riskManager.CheckExits(ltp, _context);
                return;
            }

            // Assuming _history has data:
            if (_history.Count < 30) return;

            // Calculate EMAs
            var ema5 = _history.GetEma(5).LastOrDefault()?.Ema;
            var ema8 = _history.GetEma(8).LastOrDefault()?.Ema;
            var ema13 = _history.GetEma(13).LastOrDefault()?.Ema;
            var ema21 = _history.GetEma(21).LastOrDefault()?.Ema;

            if (ema5 == null || ema21 == null) return;

            // Logic: 5 > 8 > 13 > 21 (Buy)
            if (ema5 > ema8 && ema8 > ema13 && ema13 > ema21)
            {
                Console.WriteLine($"[4EMA] Buy Signal @ {ltp}");
                await _engine.ExecuteOrderAsync(new StrategyConfig { Name = "4 EMA Strategy" }, Symbol, "BUY");
                _riskManager.InitializeEntry(ltp, _context);
            }
            // Logic: 5 < 8 < 13 < 21 (Sell)
            else if (ema5 < ema8 && ema8 < ema13 && ema13 < ema21)
            {
                Console.WriteLine($"[4EMA] Sell Signal @ {ltp}");
                await _engine.ExecuteOrderAsync(new StrategyConfig { Name = "4 EMA Strategy" }, Symbol, "SELL");
                _riskManager.InitializeEntry(ltp, _context);
            }
        }
        
        // Helper to add candles (Called by Engine)
        public void AddCandle(DateTime time, decimal open, decimal high, decimal low, decimal close, decimal volume)
        {
            _history.Add(new Quote
            {
                Date = time,
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = volume
            });
        }
    }
}
