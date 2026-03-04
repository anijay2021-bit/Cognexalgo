using System;

namespace Cognexalgo.Core.Models
{
    /// <summary>
    /// Defines a time-scheduled strategy execution:
    /// auto-enter at EntryTime, monitor per-leg SL/Target as % of premium,
    /// and force squareoff at ExitTime each trading day.
    /// </summary>
    public class ScheduledStrategyConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

        /// <summary>Strategy template name, e.g. "Short Straddle", "Short Iron Condor".</summary>
        public string StrategyName { get; set; } = "Short Straddle";

        /// <summary>Index to trade: "NIFTY" or "BANKNIFTY".</summary>
        public string Symbol { get; set; } = "NIFTY";

        /// <summary>Wall-clock time to enter. Legs fire on the first tick at or after this time.</summary>
        public TimeOnly EntryTime { get; set; } = new TimeOnly(9, 20);

        /// <summary>Wall-clock time to force-exit all open legs.</summary>
        public TimeOnly ExitTime { get; set; } = new TimeOnly(15, 15);

        /// <summary>Per-leg stop-loss as % of entry premium. 25 = SL when premium moves 25% against position.</summary>
        public double SlPercent { get; set; } = 25.0;

        /// <summary>Per-leg profit target as % of entry premium. 50 = exit when 50% of premium is captured.</summary>
        public double TargetPercent { get; set; } = 50.0;

        /// <summary>Number of lots per leg.</summary>
        public int TotalLots { get; set; } = 1;

        /// <summary>False = paper trade (safe default). True = live orders via broker API.</summary>
        public bool IsLiveMode { get; set; } = false;

        /// <summary>Scheduler-level on/off toggle. Disabled schedules are skipped on every tick.</summary>
        public bool IsActive { get; set; } = true;

        /// <summary>Date this schedule last fired. Prevents re-entry on the same calendar day.</summary>
        public DateOnly? LastFiredDate { get; set; }

        // ── Dynamic strike: Closest Premium ──────────────────────────────────

        /// <summary>
        /// Strike selection mode.
        /// ATMPoint = fixed offsets (default).
        /// ClosestPremium = select strike whose LTP is closest to <see cref="TargetPremium"/>.
        /// </summary>
        public string StrikeMode { get; set; } = "ATMPoint";  // "ATMPoint" | "ClosestPremium"

        /// <summary>Target entry premium in ₹ (used when StrikeMode = ClosestPremium).</summary>
        public double TargetPremium { get; set; } = 100.0;

        /// <summary>
        /// Premium filter operator: "~" closest | ">=" at-least | "<=" at-most.
        /// </summary>
        public string PremiumOperator { get; set; } = "~";

        /// <summary>
        /// Max ₹ deviation from TargetPremium allowed before deferring entry.
        /// 0 = always take closest regardless of distance.
        /// </summary>
        public double PremiumTolerance { get; set; } = 10.0;
    }
}
