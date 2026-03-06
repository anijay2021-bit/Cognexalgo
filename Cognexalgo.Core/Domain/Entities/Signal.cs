using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Cognexalgo.Core.Domain.Enums;

namespace Cognexalgo.Core.Domain.Entities
{
    /// <summary>
    /// Signal log record capturing the exact moment a trading signal fires.
    /// Full audit trail with indicator snapshot.
    /// </summary>
    public class Signal
    {
        [Key]
        [MaxLength(40)]
        public string SignalId { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(25)]
        public string StrategyId { get; set; } = string.Empty;

        [ForeignKey(nameof(StrategyId))]
        public Strategy? Strategy { get; set; }

        [MaxLength(35)]
        public string? LegId { get; set; }

        public SignalType SignalType { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Human-readable trigger condition string.
        /// Example: "RSI(14) CrossesBelow 30 AND Price IsBelow EMA(21)"
        /// </summary>
        [MaxLength(500)]
        public string TriggerCondition { get; set; } = string.Empty;

        /// <summary>
        /// JSON snapshot of all indicator values at signal time.
        /// Example: { "RSI": 28.4, "EMA21": 24850.5, "Price": 24830.0 }
        /// </summary>
        [Column(TypeName = "jsonb")]
        public string? IndicatorSnapshot { get; set; }

        public SignalActionTaken ActionTaken { get; set; }

        [MaxLength(60)]
        public string? OrderId { get; set; }

        [MaxLength(50)]
        public string? Symbol { get; set; }

        public double Price { get; set; }

        /// <summary>Total contracts (TotalLots × LotSize). Not persisted — flows from strategy to order engine.</summary>
        [NotMapped]
        public int Quantity { get; set; } = 1;

        /// <summary>Angel One symbol token for the traded instrument. Not persisted — flows from strategy to order engine.</summary>
        [NotMapped]
        public string SymbolToken { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? Reason { get; set; }
    }
}
