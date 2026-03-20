using System;
using System.Collections.Generic;
using Cognexalgo.Core.Domain.Patterns;

namespace Cognexalgo.Core.Models
{
    /// <summary>
    /// Specifies whether the VCP strategy executes against live broker orders
    /// or simulates trades in paper-trading mode.
    /// </summary>
    public enum VCPTradingMode
    {
        /// <summary>All orders are simulated — no real capital at risk.</summary>
        PaperTrade,

        /// <summary>Orders are routed to the live broker API.</summary>
        LiveTrade
    }

    /// <summary>
    /// The chart timeframe(s) on which the VCP scanner runs.
    /// </summary>
    public enum VCPTimeframe
    {
        /// <summary>End-of-day scan on daily candles only.</summary>
        Daily,

        /// <summary>Intraday scan on 15-minute candles only.</summary>
        FifteenMin,

        /// <summary>Run both Daily and 15-minute scans simultaneously.</summary>
        Both
    }

    /// <summary>
    /// All user-configurable parameters for the VCP strategy.
    /// Loaded and persisted by <c>IVCPSettingsService</c>.
    /// Defaults represent conservative, sensible starting values.
    /// </summary>
    public class VCPSettings
    {
        // ── Execution ──────────────────────────────────────────────────────────

        /// <summary>
        /// Whether to paper-trade or live-trade.
        /// Default: <see cref="VCPTradingMode.PaperTrade"/>.
        /// </summary>
        public VCPTradingMode TradingMode { get; set; } = VCPTradingMode.PaperTrade;

        /// <summary>
        /// The chart timeframe(s) to scan for VCP patterns.
        /// Default: <see cref="VCPTimeframe.Daily"/>.
        /// </summary>
        public VCPTimeframe Timeframe { get; set; } = VCPTimeframe.Daily;

        // ── Position sizing ────────────────────────────────────────────────────

        /// <summary>
        /// Maximum number of VCP trades that may be open simultaneously.
        /// Allowed range: 1–4. Default: 2.
        /// </summary>
        public int MaxConcurrentTrades { get; set; } = 2;

        /// <summary>
        /// Capital risked per trade in rupees, used when <see cref="UseAutoLotSizing"/> is <c>true</c>.
        /// Default: ₹1 000.
        /// </summary>
        public decimal RiskAmountPerTrade { get; set; } = 1_000m;

        /// <summary>
        /// When <c>true</c>, lot size is computed automatically from
        /// <see cref="RiskAmountPerTrade"/> and the stop-loss distance.
        /// When <c>false</c>, <see cref="FixedLotsPerTrade"/> is used instead.
        /// </summary>
        public bool UseAutoLotSizing { get; set; } = false;

        /// <summary>
        /// Fixed number of lots to trade when <see cref="UseAutoLotSizing"/> is <c>false</c>.
        /// Default: 1.
        /// </summary>
        public int FixedLotsPerTrade { get; set; } = 1;

        // ── Profit targets ─────────────────────────────────────────────────────

        /// <summary>
        /// Risk-to-reward ratio for the first profit target.
        /// Target1 = EntryPrice + (EntryPrice − StopLoss) × Target1RR.
        /// Default: 1.5×.
        /// </summary>
        public decimal Target1RR { get; set; } = 1.5m;

        /// <summary>
        /// Risk-to-reward ratio for the second profit target.
        /// Default: 3.0×.
        /// </summary>
        public decimal Target2RR { get; set; } = 3.0m;

        /// <summary>
        /// Percentage of the total position to close when Target1 is hit.
        /// Remainder rides to Target2. Default: 50 (%).
        /// </summary>
        public int Target1ExitPercent { get; set; } = 50;

        // ── Exit rules ─────────────────────────────────────────────────────────

        /// <summary>
        /// When <c>true</c>, exit the trade if the VCP base structure breaks down
        /// (price closes below <c>TightLow</c>).
        /// </summary>
        public bool ExitOnPatternFailure { get; set; } = true;

        /// <summary>
        /// Controls whether the pattern-failure exit is immediate or waits for candle close.
        /// Default: <see cref="ExitMode.Immediate"/>.
        /// </summary>
        public ExitMode PatternFailureExitMode { get; set; } = ExitMode.Immediate;

        /// <summary>
        /// When <c>true</c>, exit the trade if a bearish reversal candle forms
        /// (e.g. bearish engulfing, shooting star) after entry.
        /// </summary>
        public bool ExitOnReversalCandle { get; set; } = true;

        /// <summary>
        /// Controls whether the reversal-candle exit is immediate or waits for candle close.
        /// Default: <see cref="ExitMode.WaitForCandleClose"/>.
        /// </summary>
        public ExitMode ReversalCandleExitMode { get; set; } = ExitMode.WaitForCandleClose;

        /// <summary>
        /// When <c>true</c>, all open positions are squared off at <see cref="SquareOffTime"/>
        /// regardless of P&amp;L, to avoid carrying overnight or expiry risk.
        /// </summary>
        public bool EnableEndOfDaySquareOff { get; set; } = true;

        /// <summary>
        /// Intraday time at which the end-of-day square-off is triggered.
        /// Default: 15:10 (5 minutes before NSE equity close).
        /// </summary>
        public TimeSpan SquareOffTime { get; set; } = new TimeSpan(15, 10, 0);

        // ── Pattern quality filter ─────────────────────────────────────────────

        /// <summary>
        /// Only trade patterns whose <see cref="VCPQuality"/> is at least this grade.
        /// Patterns graded below this threshold are detected but not traded.
        /// Default: <see cref="VCPQuality.B"/> (trades A and B quality only).
        /// </summary>
        public VCPQuality MinVCPQuality { get; set; } = VCPQuality.B;

        // ── Watchlist ──────────────────────────────────────────────────────────

        /// <summary>
        /// The list of equity/index symbols the VCP scanner monitors.
        /// Each entry is a plain symbol string (e.g. "RELIANCE", "NIFTY").
        /// </summary>
        public List<string> Watchlist { get; set; } = new();

        // ── Lot sizes ──────────────────────────────────────────────────────────

        /// <summary>
        /// Lot size for NIFTY options contracts. Default: 65.
        /// Update when SEBI/NSE revises the contract size.
        /// </summary>
        public int NiftyLotSize { get; set; } = 65;

        /// <summary>
        /// Lot size for BANKNIFTY options contracts. Default: 30.
        /// Update when SEBI/NSE revises the contract size.
        /// </summary>
        public int BankNiftyLotSize { get; set; } = 30;
    }
}
