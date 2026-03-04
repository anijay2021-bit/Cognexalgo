using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Cognexalgo.Core.Services
{
    /// <summary>
    /// Fetches real-time spot (LTP) prices for index instruments via the AngelOne API.
    /// Supported symbols: NIFTY, BANKNIFTY, FINNIFTY, MIDCPNIFTY, SENSEX
    /// </summary>
    public class SpotPriceService
    {
        private readonly SmartApiClient _api;
        private readonly ApiRateLimiter _rateLimiter;

        // Angel One NSE index token map
        // Exchange: NSE (indices are on NSE, not NFO)
        private static readonly Dictionary<string, (string Token, string TradingSymbol)> _tokenMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["NIFTY"]       = ("99926000", "Nifty 50"),
                ["BANKNIFTY"]   = ("99926009", "Nifty Bank"),
                ["FINNIFTY"]    = ("99926037", "Nifty Fin Service"),
                ["MIDCPNIFTY"]  = ("99926030", "NIFTY MID SELECT"),
                ["SENSEX"]      = ("99919017", "SENSEX"),
            };

        public SpotPriceService(SmartApiClient api)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _rateLimiter = new ApiRateLimiter(maxRequestsPerSecond: 3);
        }

        /// <summary>
        /// Returns live LTP for the given index symbol.
        /// Falls back to batch market-data API if the single LTP call fails.
        /// Returns 0 if not logged in or all calls fail.
        /// </summary>
        public async Task<decimal> GetSpotAsync(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("Symbol must not be empty.", nameof(symbol));

            if (!_tokenMap.TryGetValue(symbol, out var entry))
                throw new ArgumentException($"Unsupported symbol '{symbol}'. Supported: {string.Join(", ", _tokenMap.Keys)}", nameof(symbol));

            if (_api.JwtToken == null)
                return 0m; // Not logged in — caller decides how to handle

            await _rateLimiter.WaitAsync();

            // Primary: single LTP call
            var ltpResponse = await _api.GetLTPDataAsync(
                exchange: "NSE",
                tradingSymbol: entry.TradingSymbol,
                symbolToken: entry.Token);

            if (ltpResponse?.Data?.Ltp is double ltp && ltp > 0)
                return (decimal)ltp;

            // Fallback: batch market data
            await _rateLimiter.WaitAsync();
            var batch = await _api.GetMarketDataBatchAsync("NSE", new List<string> { entry.Token });
            if (batch != null && batch.TryGetValue(entry.Token, out double batchLtp) && batchLtp > 0)
                return (decimal)batchLtp;

            throw new Exception($"Failed to fetch spot price for '{symbol}' — both LTP and batch API returned no data.");
        }

        /// <summary>
        /// Synchronous wrapper around <see cref="GetSpotAsync"/>.
        /// Blocks the calling thread. Prefer GetSpotAsync in async contexts.
        /// </summary>
        public decimal GetSpot(string symbol) =>
            GetSpotAsync(symbol).GetAwaiter().GetResult();
    }
}
