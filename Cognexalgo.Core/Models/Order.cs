using System;
using Newtonsoft.Json;

namespace Cognexalgo.Core.Models
{
    public class Order
    {
        [JsonProperty("orderid")]
        public string OrderId { get; set; }

        [JsonProperty("tradingsymbol")]
        public string Symbol { get; set; }

        [JsonProperty("symboltoken")]
        public string Token { get; set; }

        [JsonProperty("quantity")]
        public double Qty { get; set; }

        [JsonProperty("price")]
        public double Price { get; set; }

        [JsonProperty("status")] // e.g., "complete", "rejected", "open"
        public string Status { get; set; } 

        [JsonProperty("transactiontype")] // BUY, SELL
        public string TransactionType { get; set; } 
        
        [JsonProperty("updatetime")]
        public string UpdateTime { get; set; } // API returns string, we might need to parse it

        private DateTime _timestamp;
        public DateTime Timestamp
        {
            get => _timestamp != default
                       ? _timestamp
                       : (DateTime.TryParse(UpdateTime, out DateTime dt) ? dt : DateTime.Now);
            set => _timestamp = value;
        }

        [JsonProperty("producttype")]
        public string ProductType { get; set; } = "MIS";

        [JsonProperty("variety")]
        public string Variety { get; set; } = "NORMAL";
        
        public int StrategyId { get; set; }
        public string StrategyName { get; set; } // Tagging source

        // Live Risk Data (Phase 2)
        public double SlPrice { get; set; }
        public double TargetPrice { get; set; }
        public double TrailingHigh { get; set; }
        public bool IsProfitProtected { get; set; }

        // Analytics (Phase 3)
        public double PotentialProfit { get; set; } // Max profit reached
        public double ProtectedProfit { get; set; } // Profit at SL trigger
        public double ActualProfit { get; set; }    // Final realized profit
    }
}
