using System;

namespace Cognexalgo.Core.Data.Entities
{
    /// <summary>
    /// Historical candle stored in LiteDB local cache.
    /// Id is a composite string key: "{Symbol}|{Interval}|{Timestamp.Ticks}"
    /// allowing O(1) upsert without a secondary lookup.
    /// </summary>
    public class CachedCandle
    {
        /// <summary>Composite primary key for LiteDB upsert: Symbol|Interval|Ticks</summary>
        public string Id { get; set; }

        public string Symbol    { get; set; }
        public string Interval  { get; set; }
        public DateTime Timestamp { get; set; }

        public decimal Open   { get; set; }
        public decimal High   { get; set; }
        public decimal Low    { get; set; }
        public decimal Close  { get; set; }
        public decimal Volume { get; set; }

        public DateTime LastUpdated { get; set; }

        /// <summary>Build the composite key from parts.</summary>
        public static string MakeId(string symbol, string interval, DateTime ts)
            => $"{symbol}|{interval}|{ts.Ticks}";
    }
}
