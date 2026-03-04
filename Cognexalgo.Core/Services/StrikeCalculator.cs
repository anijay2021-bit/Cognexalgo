using System;
using System.Collections.Generic;

namespace Cognexalgo.Core.Services
{
    /// <summary>
    /// Calculates ATM and offset strikes for index options.
    /// </summary>
    public static class StrikeCalculator
    {
        /// <summary>Default strike step per symbol.</summary>
        private static readonly Dictionary<string, int> _strikeSteps =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["NIFTY"]      = 50,
                ["BANKNIFTY"]  = 100,
                ["FINNIFTY"]   = 50,
                ["MIDCPNIFTY"] = 25,
                ["SENSEX"]     = 100,
            };

        /// <summary>
        /// Returns the default strike step for a known symbol, or throws for unknown ones.
        /// </summary>
        public static int GetStrikeStep(string symbol)
        {
            if (_strikeSteps.TryGetValue(symbol, out int step))
                return step;
            throw new ArgumentException($"Unknown symbol '{symbol}'. Supported: {string.Join(", ", _strikeSteps.Keys)}", nameof(symbol));
        }

        /// <summary>
        /// Returns the ATM strike nearest to <paramref name="spot"/> using the given <paramref name="strikeStep"/>.
        /// Formula: Round(spot / step) * step
        /// </summary>
        public static decimal GetATMStrike(decimal spot, int strikeStep)
        {
            if (strikeStep <= 0)
                throw new ArgumentOutOfRangeException(nameof(strikeStep), "Strike step must be positive.");

            return Math.Round(spot / strikeStep) * strikeStep;
        }

        /// <summary>
        /// Convenience overload — resolves the strike step from the symbol name.
        /// </summary>
        public static decimal GetATMStrike(decimal spot, string symbol) =>
            GetATMStrike(spot, GetStrikeStep(symbol));
    }
}
