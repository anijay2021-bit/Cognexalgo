using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Cognexalgo.Core.Data.Entities
{
    [Table("client_sessions")]
    public class ClientSession
    {
        [Key]
        public Guid Id { get; set; }

        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }

        public decimal TotalPnL { get; set; }
        public int TradeCount { get; set; }

        public string StrategiesUsed { get; set; } // JSON or Comma-separated
        public string MachineName { get; set; }
        
        public string Status { get; set; } // "ACTIVE", "COMPLETED", "CRASHED"
    }

    [Table("trade_history")]
    public class TradeHistory
    {
        [Key]
        public Guid Id { get; set; }

        public Guid SessionId { get; set; }

        public string Symbol { get; set; }
        public int Quantity { get; set; }
        
        public decimal EntryPrice { get; set; }
        public decimal ExitPrice { get; set; }
        public decimal PnL { get; set; }

        public string StrategyName { get; set; }
        public string TransactionType { get; set; } // BUY or SELL

        public DateTime EntryTime { get; set; }
        public DateTime ExitTime { get; set; }
    }
}
