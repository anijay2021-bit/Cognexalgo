using System;

namespace Cognexalgo.Core.Models
{
    /// <summary>
    /// Records a single significant event in a Calendar Strategy session
    /// (entry, roll, SL hit, flip, hedge buy/exit, completion).
    /// These records are stored in CalendarStrategyState.PerformanceLog
    /// and drive the Performance Dashboard metrics.
    /// </summary>
    public class CalendarPerformanceRecord
    {
        public DateTime Date            { get; set; }

        /// <summary>ENTRY / ROLL / SL_HIT / FLIP_BUY / FLIP_SELL /
        /// HEDGE_BUY / HEDGE_EXIT / EXIT</summary>
        public string   EventType       { get; set; } = "";

        public string   LegDescription  { get; set; } = "";
        public double   EntryPrice      { get; set; }
        public double   ExitPrice       { get; set; }

        /// <summary>Realised P&amp;L impact of this single event (₹).</summary>
        public double   PnL             { get; set; }

        /// <summary>Running cumulative realised P&amp;L after this event.</summary>
        public double   CumulativePnL   { get; set; }

        /// <summary>True when a hedge was active at the time of this event.</summary>
        public bool     WasHedged       { get; set; }

        /// <summary>
        /// Premium paid for hedge legs (positive = cost to buy hedge).
        /// Non-zero only on HEDGE_BUY events.
        /// </summary>
        public double   HedgeCost       { get; set; }
    }
}
