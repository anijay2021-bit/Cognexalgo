using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Cognexalgo.Core.Domain.Enums;

namespace Cognexalgo.Core.Domain.Entities
{
    /// <summary>
    /// Individual leg of a strategy (e.g., one side of a straddle).
    /// LegId format: LEG-{StrategyId}-{LegNumber}
    /// </summary>
    public class StrategyLeg
    {
        [Key]
        [MaxLength(35)]
        public string LegId { get; set; } = string.Empty;

        // Parent Strategy
        [Required]
        [MaxLength(25)]
        public string StrategyId { get; set; } = string.Empty;

        [ForeignKey(nameof(StrategyId))]
        public Strategy? Strategy { get; set; }

        public int LegNumber { get; set; }

        // Instrument
        [Required]
        [MaxLength(50)]
        public string TradingSymbol { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? SymbolToken { get; set; }

        [MaxLength(10)]
        public string Exchange { get; set; } = "NFO";

        public InstrumentType InstrumentType { get; set; } = InstrumentType.OPTIDX;

        [MaxLength(5)]
        public string OptionType { get; set; } = "CE"; // CE, PE, FUT, EQ

        public decimal StrikePrice { get; set; }

        public DateTime? Expiry { get; set; }

        // Direction & Quantity
        public Direction Direction { get; set; }
        public int Quantity { get; set; }
        public int Lots { get; set; } = 1;

        // Status
        public LegStatus Status { get; set; } = LegStatus.PENDING;

        // Execution
        public decimal EntryPrice { get; set; }
        public decimal ExitPrice { get; set; }
        public DateTime? EntryTime { get; set; }
        public DateTime? ExitTime { get; set; }

        // Risk
        public decimal StopLossPrice { get; set; }
        public decimal TargetPrice { get; set; }
        public decimal TrailingStopLoss { get; set; }

        // P&L
        public decimal Pnl { get; set; }
        public ExitReason? ExitReason { get; set; }

        // Live Data (not persisted to DB, used at runtime)
        [NotMapped]
        public decimal Ltp { get; set; }

        [NotMapped]
        public decimal UnrealizedPnl => Direction == Direction.BUY
            ? (Ltp - EntryPrice) * Quantity
            : (EntryPrice - Ltp) * Quantity;
    }
}
