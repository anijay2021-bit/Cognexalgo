namespace Cognexalgo.Core.Models
{
    /// <summary>
    /// Lightweight leg descriptor used by the Payoff Builder.
    /// Holds user-selected parameters before being converted to a StrategyLeg
    /// for deployment into the V2 engine.
    /// </summary>
    public class StrategyLegBuilder
    {
        public string OptionType    { get; set; } = "CE";   // CE / PE
        public string Action        { get; set; } = "SELL"; // BUY / SELL
        public double Strike        { get; set; }
        public string Expiry        { get; set; } = "Weekly";
        public int    Lots          { get; set; } = 1;
        public double Premium       { get; set; }           // Entry premium (₹) – used for payoff calc
        public string TradingSymbol { get; set; } = "";
        public string Token         { get; set; } = "";
        public string Index         { get; set; } = "NIFTY";
    }
}
