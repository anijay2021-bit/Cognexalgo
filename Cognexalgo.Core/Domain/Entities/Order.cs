using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Cognexalgo.Core.Domain.Enums;

namespace Cognexalgo.Core.Domain.Entities
{
    /// <summary>
    /// Full order entity with complete audit trail.
    /// OrderId format: ORD-{StrategyId}-{LegNumber}-{Timestamp}-{SEQ}
    /// Example: ORD-STR20260128STRD001-L1-152601-001
    /// </summary>
    public class Order
    {
        // ─── Identity ─────────────────────────────────────────────
        [Key]
        [MaxLength(60)]
        public string OrderId { get; set; } = string.Empty;

        [MaxLength(30)]
        public string? BrokerOrderId { get; set; }

        [MaxLength(25)]
        public string StrategyId { get; set; } = string.Empty;

        [ForeignKey(nameof(StrategyId))]
        public Strategy? Strategy { get; set; }

        [MaxLength(35)]
        public string? LegId { get; set; }

        [ForeignKey(nameof(LegId))]
        public StrategyLeg? Leg { get; set; }

        [MaxLength(40)]
        public string? SignalId { get; set; }

        // ─── Instrument ───────────────────────────────────────────
        [Required]
        [MaxLength(50)]
        public string TradingSymbol { get; set; } = string.Empty;

        [MaxLength(10)]
        public string Exchange { get; set; } = "NFO";

        public InstrumentType InstrumentType { get; set; }

        // ─── Order Details ────────────────────────────────────────
        public Direction Direction { get; set; }
        public OrderType OrderType { get; set; } = OrderType.MARKET;
        public ProductType ProductType { get; set; } = ProductType.MIS;

        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal TriggerPrice { get; set; }

        // ─── Execution ───────────────────────────────────────────
        public OrderStatus Status { get; set; } = OrderStatus.PENDING;

        public decimal FilledPrice { get; set; }
        public int FilledQuantity { get; set; }
        public int PendingQuantity { get; set; }
        public int RejectedQuantity { get; set; }

        [MaxLength(500)]
        public string? RejectionReason { get; set; }

        // ─── Timing ──────────────────────────────────────────────
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? PlacedAt { get; set; }
        public DateTime? FilledAt { get; set; }

        /// <summary>Latency in ms from CreatedAt to PlacedAt</summary>
        public long LatencyMs { get; set; }

        // ─── Mode ────────────────────────────────────────────────
        public TradingMode TradingMode { get; set; } = TradingMode.PaperTrade;
        public bool IsSimulated { get; set; } = true;

        // ─── Retry ───────────────────────────────────────────────
        public int RetryCount { get; set; }
        public int MaxRetries { get; set; } = 3;

        [MaxLength(500)]
        public string? LastError { get; set; }

        // ─── Analytics ───────────────────────────────────────────
        public decimal PotentialProfit { get; set; }
        public decimal ProtectedProfit { get; set; }
        public decimal ActualProfit { get; set; }
    }
}
