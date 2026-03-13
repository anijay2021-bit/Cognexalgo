using System;
using System.Collections.Generic;

namespace Cognexalgo.Core.Models
{
    /// <summary>Runtime state of a running Calendar Strategy instance.</summary>
    public class CalendarStrategyState
    {
        // ── Phase tracking ───────────────────────────────────────────────────
        public CalendarPhase Phase { get; set; } = CalendarPhase.WaitingFirstEntry;

        // ── ATM Strike (same for both buy and sell legs) ─────────────────────
        public double ATMStrike { get; set; }

        // ── Buy legs (next-month straddle — held till monthly expiry) ────────
        public CalendarLeg BuyCallLeg { get; set; } = new();
        public CalendarLeg BuyPutLeg  { get; set; } = new();

        // ── Sell legs (weekly straddle — rolled each weekly expiry) ──────────
        public CalendarLeg SellCallLeg { get; set; } = new();
        public CalendarLeg SellPutLeg  { get; set; } = new();

        // ── Combined sell entry price = SL for each sell leg ─────────────────
        public double CombinedSellEntryPrice { get; set; }

        // ── Monthly expiry info ──────────────────────────────────────────────
        public DateTime MonthlyExpiry              { get; set; }
        public string   MonthlyExpirySymbolSuffix  { get; set; } = "";

        // ── Weekly expiry info ───────────────────────────────────────────────
        public DateTime CurrentWeeklyExpiry { get; set; }

        // ── MTM tracking ─────────────────────────────────────────────────────
        public double TotalRealizedPnL   { get; set; }
        public double TotalUnrealizedPnL { get; set; }
        public double TotalPnL           => TotalRealizedPnL + TotalUnrealizedPnL;

        // ── Flags ─────────────────────────────────────────────────────────────
        public bool FirstEntryDone       { get; set; }
        public bool MonthlyExpiryIsToday { get; set; }

        // ── Logs ──────────────────────────────────────────────────────────────
        public List<string> EventLog { get; set; } = new();

        public void Log(string msg)
        {
            string entry = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            EventLog.Add(entry);
            Console.WriteLine($"[CalendarStrategy] {entry}");
        }
    }

    /// <summary>Represents a single option leg inside the calendar strategy.</summary>
    public class CalendarLeg
    {
        public string   TradingSymbol     { get; set; } = "";
        public string   Token             { get; set; } = "";
        public string   OptionType        { get; set; } = "";   // "CE" or "PE"
        public string   Action            { get; set; } = "";   // "BUY" or "SELL"
        public double   Strike            { get; set; }
        public DateTime Expiry            { get; set; }
        public bool     IsWeekly          { get; set; }
        public double   EntryPrice        { get; set; }
        public double   CurrentLTP        { get; set; }
        public double   SLPrice           { get; set; }
        public string   Status            { get; set; } = "PENDING"; // PENDING/OPEN/SL_HIT/EXITED
        public string   OrderId           { get; set; } = "";
        public double   RealizedPnL       { get; set; }

        // For buy legs created after SL hit on sell leg
        public bool     IsFlippedBuyLeg   { get; set; }
        public double   FlippedBuySLPrice { get; set; }

        public double UnrealizedPnL => Status == "OPEN"
            ? (Action == "BUY"
                ? (CurrentLTP - EntryPrice)
                : (EntryPrice - CurrentLTP))
            : 0;
    }

    public enum CalendarPhase
    {
        WaitingFirstEntry,      // Before first entry time
        Active,                 // Normal running state
        WeeklyRollInProgress,   // Rolling weekly legs
        MonthlyExpiryDay,       // On monthly expiry day, waiting exit time
        Completed               // Strategy fully exited
    }
}
