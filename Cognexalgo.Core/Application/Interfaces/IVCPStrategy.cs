using System;
using System.Threading;
using System.Threading.Tasks;
using Cognexalgo.Core.Models;

namespace Cognexalgo.Core.Application.Interfaces
{
    /// <summary>
    /// Orchestrates the full VCP trading lifecycle: scanning, signal generation,
    /// order placement, position management, and exit handling.
    /// Implementations run on a background loop driven by candle events or a timer.
    /// </summary>
    public interface IVCPStrategy
    {
        /// <summary>
        /// Starts the VCP strategy loop. The strategy begins scanning the watchlist,
        /// generating signals, and managing open trades.
        /// </summary>
        /// <param name="ct">
        /// Cancellation token that stops the strategy when cancelled.
        /// The implementation must observe this token on every long-running operation.
        /// </param>
        /// <returns>A <see cref="Task"/> that completes when the strategy has fully started.</returns>
        Task StartAsync(CancellationToken ct);

        /// <summary>
        /// Gracefully stops the strategy. In-flight operations are allowed to finish;
        /// open positions are squared off according to the configured exit rules.
        /// </summary>
        /// <returns>A <see cref="Task"/> that completes when the strategy has fully stopped.</returns>
        Task StopAsync();

        /// <summary>
        /// <c>true</c> while the strategy loop is active and processing candles;
        /// <c>false</c> when stopped or not yet started.
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// Raised on the strategy's execution thread each time a new VCP breakout signal
        /// is confirmed and ready for order routing.
        /// Subscribers should dispatch UI updates to the appropriate thread.
        /// </summary>
        event EventHandler<VCPSignal> OnSignalGenerated;

        /// <summary>
        /// Raised when a VCP trade is fully closed (any exit trigger).
        /// Carries the complete trade result for P&amp;L accounting and analytics.
        /// </summary>
        event EventHandler<VCPTradeResult> OnTradeCompleted;
    }
}
