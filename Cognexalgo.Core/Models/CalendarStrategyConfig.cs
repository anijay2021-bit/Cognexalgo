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
        public int    Id         { get; set; }
        public string Name       { get; set; } = "Calendar Strategy";
        public string Symbol     { get; set; } = "NIFTY";
        public bool   IsActive   { get; set; } = true;
        public bool   IsLiveMode { get; set; } = false;

        // ── 8 Core Configurable Parameters ───────────────────────────────────

        /// <summary>Time of first entry. Format HH:mm e.g. 09:30</summary>
        public TimeSpan FirstEntryTime { get; set; } = new TimeSpan(9, 30, 0);

        /// <summary>Number of lots for all legs.</summary>
        public int Lots { get; set; } = 1;

        /// <summary>Stop-loss % applied to flipped buy legs after sell SL hit.</summary>
        public double BuySLPercent { get; set; } = 50.0;

        /// <summary>If true, buy SL fires only on candle close. If false, fires on LTP.</summary>
        public bool EnableBuySLOnCandleClose { get; set; } = false;

        /// <summary>Candle timeframe in minutes: 1, 3, 5, 10, 15, 30, 60.</summary>
        public int Timeframe { get; set; } = 5;

        /// <summary>Time to exit weekly legs and roll to next weekly. Format HH:mm e.g. 15:20</summary>
        public TimeSpan WeeklyExpiryExitTime { get; set; } = new TimeSpan(15, 20, 0);

        /// <summary>Max profit in rupees — exits all legs when hit.</summary>
        public double MaxProfit { get; set; } = 10000;

        /// <summary>Max loss in rupees (positive value) — exits all legs when hit.</summary>
        public double MaxLoss { get; set; } = 5000;

        // ── Hedge Parameters ──────────────────────────────────────────────────

        /// <summary>
        /// If true, the app buys hedge positions for weekly SELL legs
        /// 1 day before the weekly expiry AND 1 day before the monthly expiry.
        /// Hedge exits immediately when its corresponding sell leg exits.
        /// If sell leg has already flipped to BUY, no hedge is bought.
        /// </summary>
        public bool EnableHedgeBuying { get; set; } = false;

        /// <summary>
        /// Number of strikes away from the sell strike to buy hedge.
        /// Range: 1 to 10.
        /// Example: HedgeStrikeOffset=2, Sell CE 23450 → Buy CE 23550 (2×50=100 away).
        /// Example: HedgeStrikeOffset=2, Sell PE 23450 → Buy PE 23350 (2×50=100 away).
        /// </summary>
        public int HedgeStrikeOffset { get; set; } = 2;

        // ── Derived Helpers ───────────────────────────────────────────────────

        /// <summary>Lot size per symbol.</summary>
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

        /// <summary>Strike rounding step.</summary>
        public double StrikeStep => Symbol switch
        {
            "BANKNIFTY" => 100.0,
            "SENSEX"    => 100.0,
            _           => 50.0
        };
    }
}
