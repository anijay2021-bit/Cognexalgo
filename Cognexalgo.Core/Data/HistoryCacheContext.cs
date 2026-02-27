using System;
using System.IO;
using LiteDB;
using Cognexalgo.Core.Data.Entities;

namespace Cognexalgo.Core.Data
{
    /// <summary>
    /// LiteDB context for the local historical candle cache.
    /// Replaces the previous SQLite/EF Core implementation.
    /// Default location: &lt;AppBase&gt;/Data/HistoryCache.db
    /// </summary>
    public class HistoryCacheContext : IDisposable
    {
        private readonly LiteDatabase _db;

        public HistoryCacheContext(string dbPath = null)
        {
            var path = dbPath
                ?? Path.Combine(AppContext.BaseDirectory, "Data", "HistoryCache.db");

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Connection=shared allows multiple LiteDatabase instances (e.g. AngelOneDataService
            // + TradingEngine) to open the same file within the same process without file-lock
            // conflicts. LiteDB uses an internal mutex to serialise writes.
            _db = new LiteDatabase($"Filename={path};Connection=shared");
            EnsureIndexes();
        }

        /// <summary>The candles collection — Id is the composite string key.</summary>
        public ILiteCollection<CachedCandle> Candles
            => _db.GetCollection<CachedCandle>("candles");

        private void EnsureIndexes()
        {
            var col = Candles;
            col.EnsureIndex(x => x.Symbol);
            col.EnsureIndex(x => x.Interval);
            col.EnsureIndex(x => x.Timestamp);
        }

        public void Dispose() => _db?.Dispose();
    }
}
