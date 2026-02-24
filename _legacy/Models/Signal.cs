using System;

namespace Cognexalgo.Core.Models
{
    public class Signal
    {
        public DateTime Timestamp { get; set; }
        public string StrategyName { get; set; }
        public string Symbol { get; set; }
        public string SignalType { get; set; } // BUY_CE, BUY_PE, EXIT
        public double Price { get; set; }
        public string Reason { get; set; } // e.g. "EMA Crossover"

        public override string ToString()
        {
            return $"{Timestamp:HH:mm:ss} | {StrategyName} | {SignalType} {Symbol} @ {Price} ({Reason})";
        }
    }
}
