using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Cognexalgo.Core.Domain.Enums;

namespace Cognexalgo.Core.Domain.Entities
{
    /// <summary>
    /// Core strategy entity. StrategyId format: STR-{YYYYMMDD}-{TYPE}-{SEQ}
    /// Examples: STR-20260128-STRD-001, STR-20260128-CSTM-002
    /// </summary>
    public class Strategy
    {
        [Key]
        [MaxLength(25)]
        public string StrategyId { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public StrategyType Type { get; set; }

        public StrategyStatus Status { get; set; } = StrategyStatus.Draft;

        public TradingMode TradingMode { get; set; } = TradingMode.PaperTrade;

        [MaxLength(20)]
        public string UnderlyingSymbol { get; set; } = "NIFTY";

        // Navigation: Strategy legs
        public List<StrategyLeg> Legs { get; set; } = new();

        // Configuration (stored as JSON columns)
        [Column(TypeName = "jsonb")]
        public string? SignalConfigJson { get; set; }

        [Column(TypeName = "jsonb")]
        public string? RmsConfigJson { get; set; }

        [Column(TypeName = "jsonb")]
        public string? ExecutionConfigJson { get; set; }

        // Metrics (stored as JSON)
        [Column(TypeName = "jsonb")]
        public string? MetricsJson { get; set; }

        // Timestamps
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }

        [MaxLength(50)]
        public string? CreatedBy { get; set; }

        /// <summary>Can be saved as reusable template</summary>
        public bool IsTemplate { get; set; } = false;

        // Navigation: Orders and Signals for this strategy
        public List<Order> Orders { get; set; } = new();
        public List<Signal> Signals { get; set; } = new();
        public List<Trade> Trades { get; set; } = new();

        // State Machine Snapshot (persisted for crash recovery)
        [Column(TypeName = "jsonb")]
        public string? StateMachineSnapshot { get; set; }
    }
}
