using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Cognexalgo.Core.Models
{
    [Table("active_positions")]
    public class ActivePosition
    {
        [Key]
        [Column("symbol_token")]
        public string SymbolToken { get; set; }

        [Column("trading_symbol")]
        public string TradingSymbol { get; set; }

        [Column("quantity")]
        public int Quantity { get; set; }

        [Column("average_price")]
        public decimal AveragePrice { get; set; }

        [Column("ltp")]
        public decimal LTP { get; set; }

        [Column("pnl")]
        public decimal PnL { get; set; }

        [Column("product_type")]
        public string ProductType { get; set; }

        [Column("strategy_name")]
        public string StrategyName { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }
}
