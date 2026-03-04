using System;

namespace Cognexalgo.Core.Models
{
    public class OptionLeg
    {
        public OptionType OptionType { get; set; } = OptionType.Call;
        public ActionType Position { get; set; } = ActionType.Buy;
        public decimal Strike { get; set; }
        public int LotSize { get; set; }
        public decimal Premium { get; set; }
        public DateTime Expiry { get; set; }
    }
}
