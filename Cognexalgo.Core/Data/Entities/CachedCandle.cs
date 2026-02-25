using System;

namespace Cognexalgo.Core.Data.Entities
{
    /// <summary>
    /// Represents a historical candle stored in the local SQLite cache.
    /// Mimics the robust local caching observed in KK01 app.
    /// </summary>
    public class CachedCandle
    {
        public int Id { get; set; }
        
        // Composite Key Index: Symbol + Interval + Timestamp
        public string Symbol { get; set; }
        public string Interval { get; set; }
        public DateTime Timestamp { get; set; }
        
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
        
        // Metadata
        public DateTime LastUpdated { get; set; }
    }
}
