using System;

namespace Cognexalgo.Core.Models
{
    /// <summary>
    /// Configuration for the Calendar Spread Strategy.
    /// Buy next-month ATM straddle + Sell nearest-weekly ATM straddle.
    /// Rolls weekly sell legs at each weekly expiry until monthly expiry.
    /// </summary>
    public class CalendarStrategyConfig
    {
        public int    Id           { get; set; }
        public string Name         { get; set; } = "Calendar Strategy";
        public string Symbol       { get; set; } = "NIFTY";   // NIFTY / BANKNIFTY
        public bool   IsActive     { get; set; } = true;
        public bool   IsLiveMode   { get; set; } = false;

        // ── Configurable Parameters ───────────────────────────────────────────

        /// <summary>Time of first entry on strategy creation day.</summary>
        public TimeSpan FirstEntryTime { get; set; } = new TimeSpan(9, 30, 0);

        /// <summary>Number of lots for both buy and sell legs.</summary>
        public int Lots { get; set; } = 1;

        /// <summary>Stop-loss % for individual buy legs (after CE/PE hits SL and flips to buy).</summary>
        public double BuySLPercent { get; set; } = 50.0;

        /// <summary>If true, buy SL is checked only on candle close. If false, checked on every tick.</summary>
        public bool EnableBuySLOnCandleClose { get; set; } = false;

        /// <summary>Candle timeframe in minutes for SL check. Options: 1,3,5,10,15,30,60.</summary>
        public int Timeframe { get; set; } = 5;

        /// <summary>Time to exit weekly expiry positions and roll to next weekly.</summary>
        public TimeSpan WeeklyExpiryExitTime { get; set; } = new TimeSpan(15, 20, 0);

        /// <summary>Max profit in rupees. Strategy exits all positions when hit.</summary>
        public double MaxProfit { get; set; } = 10000;

        /// <summary>Max loss in rupees (positive value). Strategy exits all positions when hit.</summary>
        public double MaxLoss { get; set; } = 5000;

        // ── Lot Size (auto-set based on symbol) ──────────────────────────────
        public int LotSize => Symbol switch
        {
            "BANKNIFTY"  => 15,
            "FINNIFTY"   => 40,
            "MIDCPNIFTY" => 50,
            "SENSEX"     => 20,
            _            => 75   // NIFTY default
        };

        /// <summary>Total quantity per leg = Lots × LotSize.</summary>
        public int TotalQty => Lots * LotSize;

        // ── Strike rounding step ─────────────────────────────────────────────
        public double StrikeStep => Symbol switch
        {
            "BANKNIFTY" => 100.0,
            "SENSEX"    => 100.0,
            _           => 50.0
        };
    }
}
