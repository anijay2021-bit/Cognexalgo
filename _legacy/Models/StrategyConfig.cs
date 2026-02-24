using System;

namespace Cognexalgo.Core.Models
{
    public class StrategyConfig
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Symbol { get; set; }
        public string ProductType { get; set; } = "MIS";
        public string StrategyType { get; set; } // "4EMA", "SPREAD", "CUSTOM"
        public string Parameters { get; set; } // JSON string
        public bool IsActive { get; set; }
    }
}
