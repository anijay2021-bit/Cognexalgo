using System;
using System.Collections.Generic;

namespace Cognexalgo.Core.Models
{
    /// <summary>Runtime state of a running Calendar Strategy instance.</summary>
    public class CalendarStrategyState
    {
        public CalendarPhase Phase                { get; set; } = CalendarPhase.WaitingFirstEntry;
        public double        ATMStrike            { get; set; }
        public DateTime      MonthlyExpiry        { get; set; }
        public DateTime      CurrentWeeklyExpiry  { get; set; }
        public bool          FirstEntryDone       { get; set; }
        public bool          MonthlyExpiryIsToday { get; set; }

        // ── Core Legs ─────────────────────────────────────────────────────────
        public CalendarLeg BuyCallLeg  { get; set; } = new();
        public CalendarLeg BuyPutLeg   { get; set; } = new();
        public CalendarLeg SellCallLeg { get; set; } = new();
        public CalendarLeg SellPutLeg  { get; set; } = new();

        // ── Hedge Legs (1:1 tied to SellCallLeg and SellPutLeg) ──────────────
        // HedgeCallLeg is the BUY CE bought above the Sell CE strike.
        // HedgePutLeg  is the BUY PE bought below the Sell PE strike.
        // Both exit IMMEDIATELY when their corresponding sell leg exits,
        // regardless of reason (SL / roll / max profit / max loss / manual).
        public CalendarLeg HedgeCallLeg { get; set; } = new();
        public CalendarLeg HedgePutLeg  { get; set; } = new();

        /// <summary>
        /// True once hedge has been bought for the current weekly expiry cycle.
        /// Reset to false after weekly roll so hedges are re-bought next cycle.
        /// </summary>
        public bool HedgeBought { get; set; } = false;

        /// <summary>
        /// Combined sell entry price = SellCall.EntryPrice + SellPut.EntryPrice.
        /// This is the SL trigger price for EACH individual sell leg.
        /// </summary>
        public double CombinedSellEntryPrice { get; set; }

        // ── P&L ───────────────────────────────────────────────────────────────
        public double TotalRealizedPnL   { get; set; }
        public double TotalUnrealizedPnL { get; set; }
        public double TotalPnL           => TotalRealizedPnL + TotalUnrealizedPnL;

        // ── Event Log ─────────────────────────────────────────────────────────
        public List<string> EventLog { get; set; } = new();

        public void Log(string msg)
        {
            string entry = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            EventLog.Add(entry);
            Console.WriteLine($"[CalendarStrategy] {entry}");
        }
    }

    /// <summary>
    /// A single option leg. Property names match OptionChainItem exactly:
    /// TradingSymbol (alias for Symbol), Token, OptionType, Strike,
    /// ExpiryDate, IsWeeklyExpiry, LTP, IsCall, IsPut.
    /// </summary>
    public class CalendarLeg
    {
        public string   TradingSymbol  { get; set; } = "";
        public string   Token          { get; set; } = "";
        public string   OptionType     { get; set; } = ""; // "CE" or "PE"
        public string   Action         { get; set; } = ""; // "BUY" or "SELL"
        public double   Strike         { get; set; }
        public DateTime ExpiryDate     { get; set; }
        public bool     IsWeeklyExpiry { get; set; }
        public double   EntryPrice     { get; set; }
        public double   CurrentLTP     { get; set; }
        public double   SLPrice        { get; set; }
        public string   Status         { get; set; } = "PENDING";
        public string   OrderId        { get; set; } = "";
        public double   RealizedPnL    { get; set; }

        /// <summary>True when this leg was originally a SELL that flipped to BUY after SL hit.</summary>
        public bool IsFlippedBuyLeg { get; set; }

        /// <summary>True when this leg is a hedge (bought for margin benefit).</summary>
        public bool IsHedgeLeg { get; set; }

        public double UnrealizedPnL =>
            Status == "OPEN" && EntryPrice > 0 && CurrentLTP > 0
                ? (Action == "BUY"
                    ? (CurrentLTP - EntryPrice)
                    : (EntryPrice - CurrentLTP))
                : 0;
    }

    public enum CalendarPhase
    {
        WaitingFirstEntry,
        Active,
        WeeklyRollInProgress,
        MonthlyExpiryDay,
        Completed
    }
}
