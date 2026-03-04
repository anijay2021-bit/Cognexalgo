using System;
using System.Collections.Generic;
using Cognexalgo.Core.Models;

namespace Cognexalgo.Core.Services
{
    /// <summary>
    /// Calculates at-expiry P&amp;L payoff points for a multi-leg option strategy.
    /// Time value is zero — only intrinsic value is used.
    /// </summary>
    public static class PayoffEngine
    {
        /// <summary>
        /// Calculates payoff P&amp;L for expiry prices ranging from
        /// <paramref name="spot"/> − 2000 to <paramref name="spot"/> + 2000.
        /// </summary>
        /// <param name="legs">Option legs (must have Premium populated).</param>
        /// <param name="spot">Current underlying spot price.</param>
        /// <param name="step">Price increment between data points (default 50).</param>
        public static List<PayoffPoint> Calculate(
            IReadOnlyList<OptionLeg> legs,
            decimal spot,
            int step = 50)
        {
            if (legs == null || legs.Count == 0)
                throw new ArgumentException("Legs list must not be empty.", nameof(legs));
            if (step <= 0)
                throw new ArgumentOutOfRangeException(nameof(step), "Step must be positive.");

            var points = new List<PayoffPoint>();

            for (decimal price = spot - 2000; price <= spot + 2000; price += step)
            {
                double pnl = 0.0;

                foreach (var leg in legs)
                {
                    // At expiry, option value = intrinsic value only
                    double intrinsic = leg.OptionType == OptionType.Call
                        ? Math.Max(0.0, (double)(price - leg.Strike))
                        : Math.Max(0.0, (double)(leg.Strike - price));

                    double legPnl = leg.Position == ActionType.Sell
                        ? ((double)leg.Premium - intrinsic) * leg.LotSize
                        : (intrinsic - (double)leg.Premium) * leg.LotSize;

                    pnl += legPnl;
                }

                points.Add(new PayoffPoint { Price = (double)price, Pnl = pnl });
            }

            return points;
        }
    }
}
