using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cognexalgo.Core.Models;

namespace Cognexalgo.Core.Application.Interfaces
{
    /// <summary>
    /// Provides live candle streaming and historical candle retrieval.
    /// Implementations bridge the SmartStream WebSocket feed to domain events.
    /// </summary>
    public interface IMarketDataService
    {
        /// <summary>
        /// Raised each time a candle closes on any subscribed symbol/timeframe.
        /// </summary>
        event Action<Candle>? OnCandleFormed;

        /// <summary>
        /// Subscribe to the live candle feed for a symbol and timeframe.
        /// </summary>
        Task SubscribeCandlesAsync(string symbol, string timeframe, CancellationToken ct = default);

        /// <summary>
        /// Unsubscribe from the live candle feed for a symbol and timeframe.
        /// </summary>
        Task UnsubscribeCandlesAsync(string symbol, string timeframe);

        /// <summary>
        /// Fetches the most recent <paramref name="count"/> completed candles for the given symbol/timeframe.
        /// Returns an empty list (never null) if no data is available.
        /// </summary>
        Task<List<Candle>> GetCandlesAsync(string symbol, string timeframe, int count,
                                           CancellationToken ct = default);
    }
}
