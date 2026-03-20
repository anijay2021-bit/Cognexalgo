using System;
using Cognexalgo.Core.Domain.Patterns;

namespace Cognexalgo.Core.Models
{
    /// <summary>
    /// Records the outcome of a completed VCP trade.
    /// Used for P&amp;L reporting, backtesting analysis, and strategy performance metrics.
    /// </summary>
    public class VCPTradeResult
    {
        /// <summary>
        /// The <see cref="VCPSignal.Id"/> of the signal that initiated this trade.
        /// </summary>
        public Guid SignalId { get; init; }

        /// <summary>
        /// The underlying equity or index symbol (e.g. "RELIANCE", "NIFTY").
        /// </summary>
        public string Symbol { get; init; } = string.Empty;

        /// <summary>
        /// The price at which the position was entered.
        /// </summary>
        public decimal EntryPrice { get; init; }

        /// <summary>
        /// The price at which the position was fully exited.
        /// </summary>
        public decimal ExitPrice { get; init; }

        /// <summary>
        /// The number of shares or lots traded.
        /// </summary>
        public int Quantity { get; init; }

        /// <summary>
        /// Gross profit or loss for the trade.
        /// Calculated as <c>(ExitPrice − EntryPrice) × Quantity</c>.
        /// A positive value indicates a profit; negative indicates a loss.
        /// </summary>
        public decimal PnL => (ExitPrice - EntryPrice) * Quantity;

        /// <summary>
        /// The condition or event that caused the position to be closed.
        /// </summary>
        public ExitTrigger ExitTrigger { get; init; }

        /// <summary>
        /// The date and time at which the entry order was executed.
        /// </summary>
        public DateTime EntryTime { get; init; }

        /// <summary>
        /// The date and time at which the exit order was executed.
        /// </summary>
        public DateTime ExitTime { get; init; }

        /// <summary>
        /// <c>true</c> when the trade closed with a positive P&amp;L; <c>false</c> otherwise.
        /// </summary>
        public bool IsWinner => PnL > 0;
    }
}
