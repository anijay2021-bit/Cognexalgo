using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cognexalgo.Core.Data;
using Cognexalgo.Core.Data.Entities;
using Skender.Stock.Indicators;

namespace Cognexalgo.Core.Services
{
    /// <summary>
    /// Persists and retrieves historical OHLCV candles using LiteDB.
    /// Replaces the previous SQLite/EF Core implementation.
    /// Thread-safe for concurrent reads; writes are serialised by LiteDB internally.
    /// </summary>
    public class HistoryCacheService : IDisposable
    {
        private readonly HistoryCacheContext _ctx;

        public HistoryCacheService(string dbPath = null)
        {
            _ctx = new HistoryCacheContext(dbPath);
        }

        // ── Read ────────────────────────────────────────────────────────────────

        /// <summary>Return all cached candles for a symbol+interval within the last <paramref name="days"/> days.</summary>
        public Task<List<Quote>> GetHistoryAsync(string symbol, string interval, int days)
        {
            var cutoff = DateTime.Now.AddDays(-days);

            var quotes = _ctx.Candles
                .Find(c => c.Symbol == symbol && c.Interval == interval && c.Timestamp >= cutoff)
                .OrderBy(c => c.Timestamp)
                .Select(ToQuote)
                .ToList();

            return Task.FromResult(quotes);
        }

        // ── Bulk Write (used by daily download protocol) ─────────────────────

        /// <summary>
        /// Upsert a batch of quotes into the cache.
        /// Uses the composite string Id for O(1) insert-or-update.
        /// </summary>
        public Task SaveHistoryAsync(string symbol, string interval, List<Quote> quotes)
        {
            if (quotes == null || quotes.Count == 0) return Task.CompletedTask;

            var candles = quotes.Select(q => ToEntity(symbol, interval, q)).ToList();
            _ctx.Candles.Upsert(candles);

            return Task.CompletedTask;
        }

        // ── Single candle upsert (used by live CandleAggregator) ─────────────

        /// <summary>Upsert a single completed live candle.</summary>
        public void UpsertCandle(string symbol, string interval, Quote q)
        {
            _ctx.Candles.Upsert(ToEntity(symbol, interval, q));
        }

        // ── Maintenance ──────────────────────────────────────────────────────

        /// <summary>Delete candles older than <paramref name="retentionDays"/> days.</summary>
        public Task PurgeOldDataAsync(int retentionDays = 60)
        {
            var cutoff = DateTime.Now.AddDays(-retentionDays);
            _ctx.Candles.DeleteMany(c => c.Timestamp < cutoff);
            return Task.CompletedTask;
        }

        public void Dispose() => _ctx.Dispose();

        // ── Helpers ──────────────────────────────────────────────────────────

        private static CachedCandle ToEntity(string symbol, string interval, Quote q) => new CachedCandle
        {
            Id          = CachedCandle.MakeId(symbol, interval, q.Date),
            Symbol      = symbol,
            Interval    = interval,
            Timestamp   = q.Date,
            Open        = q.Open,
            High        = q.High,
            Low         = q.Low,
            Close       = q.Close,
            Volume      = q.Volume,
            LastUpdated = DateTime.Now
        };

        private static Quote ToQuote(CachedCandle c) => new Quote
        {
            Date   = c.Timestamp,
            Open   = c.Open,
            High   = c.High,
            Low    = c.Low,
            Close  = c.Close,
            Volume = c.Volume
        };
    }
}
