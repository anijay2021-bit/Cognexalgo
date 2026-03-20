using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cognexalgo.Core;
using Cognexalgo.Core.Application.Interfaces;
using Cognexalgo.Core.Models;

namespace Cognexalgo.UI.VCP
{
    /// <summary>
    /// Bridges <see cref="TradingEngine"/>'s <see cref="Core.Services.CandleAggregator"/> infrastructure
    /// to the <see cref="IMarketDataService"/> interface consumed by VCP strategy components.
    ///
    /// Aggregator key format used by TradingEngine: "SYMBOL|TIMEFRAME" (e.g. "NIFTY|15min").
    /// </summary>
    internal sealed class TradingEngineMarketDataAdapter : IMarketDataService
    {
        private readonly TradingEngine _engine;

        // Tracks aggregator keys already subscribed so we don't double-attach.
        private readonly HashSet<string> _attached = new(StringComparer.OrdinalIgnoreCase);

        public event Action<Candle>? OnCandleFormed;

        public TradingEngineMarketDataAdapter(TradingEngine engine)
        {
            _engine = engine;

            // Attach to any aggregators that already exist at construction time.
            foreach (var kvp in engine.Aggregators)
                AttachIfNew(kvp.Key, kvp.Value);
        }

        // ── IMarketDataService ────────────────────────────────────────────────

        public Task SubscribeCandlesAsync(string symbol, string timeframe,
                                          CancellationToken ct = default)
        {
            var key = $"{symbol.ToUpperInvariant()}|{timeframe}";
            if (_engine.Aggregators.TryGetValue(key, out var agg))
                AttachIfNew(key, agg);

            // TradingEngine manages SmartStream subscriptions independently.
            // We do not duplicate that work here.
            return Task.CompletedTask;
        }

        public Task UnsubscribeCandlesAsync(string symbol, string timeframe)
        {
            // Aggregator lifetime is controlled by TradingEngine — no-op here.
            return Task.CompletedTask;
        }

        public Task<List<Candle>> GetCandlesAsync(string symbol, string timeframe,
                                                   int count,
                                                   CancellationToken ct = default)
        {
            var key = $"{symbol.ToUpperInvariant()}|{timeframe}";
            var result = new List<Candle>();

            if (!_engine.Aggregators.TryGetValue(key, out var agg))
                return Task.FromResult(result);

            var series = agg.GetFullSeries();
            foreach (var q in series.TakeLast(count))
            {
                result.Add(new Candle
                {
                    Symbol    = symbol.ToUpperInvariant(),
                    Open      = q.Open,
                    High      = q.High,
                    Low       = q.Low,
                    Close     = q.Close,
                    Volume    = (long)q.Volume,
                    Timestamp = q.Date,
                    Timeframe = timeframe
                });
            }

            return Task.FromResult(result);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void AttachIfNew(string key, Core.Services.CandleAggregator agg)
        {
            if (!_attached.Add(key)) return;   // already subscribed

            // key = "SYMBOL|TIMEFRAME"
            var parts     = key.Split('|');
            var symbol    = parts.Length > 0 ? parts[0] : key;
            var timeframe = parts.Length > 1 ? parts[1] : string.Empty;

            agg.OnCandleClosed += quote =>
            {
                OnCandleFormed?.Invoke(new Candle
                {
                    Symbol    = symbol,
                    Open      = quote.Open,
                    High      = quote.High,
                    Low       = quote.Low,
                    Close     = quote.Close,
                    Volume    = (long)quote.Volume,
                    Timestamp = quote.Date,
                    Timeframe = timeframe
                });
            };
        }
    }
}
