namespace Cognexalgo.Core.Models
{
    /// <summary>A single price/P&amp;L data point on a payoff diagram.</summary>
    public class PayoffPoint
    {
        public double Price { get; init; }
        public double Pnl   { get; init; }
    }
}
