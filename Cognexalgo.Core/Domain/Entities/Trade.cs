using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Cognexalgo.Core.Domain.Enums;

namespace Cognexalgo.Core.Domain.Entities
{
    /// <summary>
    /// Entry/Exit paired trade record for analytics.
    /// A Trade links an entry and exit together with P&L calculations.
    /// </summary>
    public class Trade
    {
        [Key]
        [MaxLength(40)]
        public string TradeId { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(25)]
        public string StrategyId { get; set; } = string.Empty;

        [ForeignKey(nameof(StrategyId))]
        public Strategy? Strategy { get; set; }

        [MaxLength(35)]
        public string? LegId { get; set; }

        [Required]
        [MaxLength(50)]
        public string Symbol { get; set; } = string.Empty;

        public Direction Direction { get; set; }
        public int Quantity { get; set; }

        public decimal EntryPrice { get; set; }
        public decimal ExitPrice { get; set; }

        public decimal GrossPnl { get; set; }
        public decimal Charges { get; set; }
        public decimal NetPnl { get; set; }

        public DateTime EntryTime { get; set; }
        public DateTime? ExitTime { get; set; }

        public TradingMode TradingMode { get; set; }
    }
}
