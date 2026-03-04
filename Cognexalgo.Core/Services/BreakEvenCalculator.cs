using System;
using System.Collections.Generic;
using System.Linq;
using Cognexalgo.Core.Models;

namespace Cognexalgo.Core.Services
{
    /// <summary>Result of a breakeven calculation.</summary>
    public class BreakEvenResult
    {
        /// <summary>Upper breakeven = ShortCallStrike + NetPremium. 0 if no short call leg.</summary>
        public decimal UpperBE { get; init; }

        /// <summary>Lower breakeven = ShortPutStrike - NetPremium. 0 if no short put leg.</summary>
        public decimal LowerBE { get; init; }

        /// <summary>Net premium collected: sum of sell premiums minus sum of buy premiums.</summary>
        public decimal NetPremium { get; init; }
    }

    /// <summary>
    /// Calculates breakeven points for credit (and debit) option strategies.
    /// <para>
    /// Upper BE = lowest-strike short call + NetPremium<br/>
    /// Lower BE = highest-strike short put  - NetPremium
    /// </para>
    /// </summary>
    public static class BreakEvenCalculator
    {
        /// <summary>
        /// Computes breakeven levels from a list of option legs.
        /// Legs must have <see cref="OptionLeg.Premium"/> populated (live LTP or fill price).
        /// </summary>
        public static BreakEvenResult Calculate(IReadOnlyList<OptionLeg> legs)
        {
            if (legs == null || legs.Count == 0)
                throw new ArgumentException("Legs list must not be empty.", nameof(legs));

            // Net premium: credits collected (Sell) minus debits paid (Buy), weighted by lot size
            decimal netPremium = legs.Sum(l =>
                l.Position == ActionType.Sell
                    ?  l.Premium * l.LotSize
                    : -l.Premium * l.LotSize);

            // Inner short call = sold call with the minimum (closest-to-ATM) strike
            var shortCalls = legs.Where(l => l.Position == ActionType.Sell
                                          && l.OptionType == OptionType.Call).ToList();

            // Inner short put = sold put with the maximum (closest-to-ATM) strike
            var shortPuts  = legs.Where(l => l.Position == ActionType.Sell
                                          && l.OptionType == OptionType.Put).ToList();

            decimal upperBE = shortCalls.Count > 0
                ? shortCalls.Min(l => l.Strike) + netPremium
                : 0m;

            decimal lowerBE = shortPuts.Count > 0
                ? shortPuts.Max(l => l.Strike) - netPremium
                : 0m;

            return new BreakEvenResult
            {
                UpperBE    = upperBE,
                LowerBE    = lowerBE,
                NetPremium = netPremium,
            };
        }
    }
}
