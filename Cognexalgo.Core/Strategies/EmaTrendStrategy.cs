using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cognexalgo.Core.Models;
using Skender.Stock.Indicators;

namespace Cognexalgo.Core.Strategies
{
    public class EmaTrendStrategy : StrategyBase
    {
        private List<Quote> _history = new List<Quote>();
        
        public string Symbol { get; set; } = "NIFTY";
        public int FastEmaPeriod { get; set; } = 9;
        public int TrendEmaPeriod { get; set; } = 200;

        public EmaTrendStrategy(TradingEngine engine) : base(engine, "EMA + 200 Trend") 
        { 
        }

        public override Task OnTickAsync(TickerData ticker)
        {
            if (!IsActive) return Task.CompletedTask;
            if (_history.Count < 2) return Task.CompletedTask; // Need at least 2 for some checks, but don't block on 200
            if (_history.Count < TrendEmaPeriod)
            {
                // Just log once a minute or so instead of every tick to avoid spam
                if (DateTime.Now.Second == 0)
                   Console.WriteLine($"[Strategy] {Name} waiting for trend data. Current: {_history.Count}/{TrendEmaPeriod}");
            }

            var lastCandle = _history.Last();
            var fastEma = _history.GetEma(FastEmaPeriod).LastOrDefault()?.Ema;
            var trendEma = _history.GetEma(TrendEmaPeriod).LastOrDefault()?.Ema;

            if (fastEma == null || trendEma == null) return Task.CompletedTask;

            // Logic: Bullish if Price > 200 EMA AND Price > 9 EMA
            bool isBullish = (double)lastCandle.Close > (double)trendEma && (double)lastCandle.Close > (double)fastEma;

            if (isBullish)
            {
               // Debug.WriteLine("Bullish Trend Detected");
               // Place Order Logic
            }
            return Task.CompletedTask;
        }
        
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
