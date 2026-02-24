using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Cognexalgo.Core.Domain.Enums;

namespace Cognexalgo.Core.Domain.Entities
{
    /// <summary>RMS breach/action log entry.</summary>
    public class RmsLog
    {
        [Key]
        public long Id { get; set; }

        [MaxLength(25)]
        public string? StrategyId { get; set; }

        public RmsRuleType RuleType { get; set; }

        public decimal BreachValue { get; set; }
        public decimal ThresholdValue { get; set; }

        public RmsAction Action { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Structured event log entry (Serilog sink target).</summary>
    public class EventLog
    {
        [Key]
        public long Id { get; set; }

        [MaxLength(10)]
        public string Level { get; set; } = "INFO";

        [MaxLength(25)]
        public string? StrategyId { get; set; }

        [MaxLength(2000)]
        public string Message { get; set; } = string.Empty;

        [Column(TypeName = "jsonb")]
        public string? Properties { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Instrument master cache. Refreshed daily from Angel One instruments URL.
    /// </summary>
    public class InstrumentMaster
    {
        [Key]
        [MaxLength(20)]
        public string Token { get; set; } = string.Empty;

        [MaxLength(60)]
        public string Symbol { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Name { get; set; }

        [MaxLength(10)]
        public string Exchange { get; set; } = "NFO";

        public InstrumentType InstrumentType { get; set; }

        public int LotSize { get; set; }
        public decimal TickSize { get; set; }
        public decimal StrikePrice { get; set; }

        public DateTime? Expiry { get; set; }

        [MaxLength(5)]
        public string? OptionType { get; set; } // CE, PE

        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Daily P&L summary per strategy.</summary>
    public class DailyPnlSummary
    {
        [Key]
        public long Id { get; set; }

        public DateTime Date { get; set; }

        [MaxLength(25)]
        public string StrategyId { get; set; } = string.Empty;

        public decimal TotalPnl { get; set; }
        public int TotalTrades { get; set; }
        public int WinCount { get; set; }
        public int LossCount { get; set; }
        public decimal MaxDrawdown { get; set; }

        public TradingMode TradingMode { get; set; }
    }

    /// <summary>Candle (OHLCV) data, partitioned by date.</summary>
    public class Candle
    {
        [Key]
        public long Id { get; set; }

        [MaxLength(50)]
        public string Symbol { get; set; } = string.Empty;

        public TimeFrame TimeFrame { get; set; }

        public DateTime Timestamp { get; set; }

        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public long Volume { get; set; }
    }

    /// <summary>Raw tick data. Keep last 7 days only.</summary>
    public class Tick
    {
        [Key]
        public long Id { get; set; }

        [MaxLength(50)]
        public string Symbol { get; set; } = string.Empty;

        public DateTime Timestamp { get; set; }

        public decimal Ltp { get; set; }
        public long Volume { get; set; }
        public long OpenInterest { get; set; }
    }

    /// <summary>Encrypted broker credentials storage.</summary>
    public class BrokerCredential
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(30)]
        public string BrokerName { get; set; } = "AngelOne";

        [MaxLength(500)]
        public string? EncryptedApiKey { get; set; }

        [MaxLength(500)]
        public string? EncryptedClientCode { get; set; }

        [MaxLength(500)]
        public string? EncryptedPassword { get; set; }

        [MaxLength(500)]
        public string? EncryptedTotpKey { get; set; }

        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}
