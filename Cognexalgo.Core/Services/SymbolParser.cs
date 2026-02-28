using System;
using System.Globalization;

namespace Cognexalgo.Core.Services
{
    /// <summary>
    /// Parses Angel One NFO option trading symbols into components.
    /// Format: {INDEX}{DDMMMYY}{STRIKE}{CE|PE}
    /// Example: NIFTY28FEB2625000CE → NIFTY, 28-Feb-2026, 25000, Call
    /// </summary>
    public static class SymbolParser
    {
        // Ordered longest-first so "BANKNIFTY" matches before "NIFTY"
        private static readonly string[] KnownIndices =
            { "BANKNIFTY", "MIDCPNIFTY", "FINNIFTY", "SENSEX", "NIFTY" };

        /// <summary>
        /// Parse an Angel One option TradingSymbol into its components.
        /// Returns false if not a recognized option symbol.
        /// </summary>
        public static bool TryParse(string tradingSymbol,
            out string index, out DateTime expiry, out int strike, out bool isCall)
        {
            index = "";
            expiry = DateTime.MinValue;
            strike = 0;
            isCall = true;

            if (string.IsNullOrEmpty(tradingSymbol))
                return false;

            string upper = tradingSymbol.ToUpperInvariant();

            // 1. Extract CE/PE suffix
            if (upper.EndsWith("CE"))
            {
                isCall = true;
                upper = upper[..^2];
            }
            else if (upper.EndsWith("PE"))
            {
                isCall = false;
                upper = upper[..^2];
            }
            else
            {
                return false; // not an option
            }

            // 2. Strip known index prefix (longest match first)
            foreach (var idx in KnownIndices)
            {
                if (upper.StartsWith(idx))
                {
                    index = idx;
                    upper = upper[idx.Length..];
                    break;
                }
            }
            if (string.IsNullOrEmpty(index))
                return false;

            // 3. Remaining: date + strike digits
            // Try 7-char date (DDMMMYY) first, then 9-char (DDMMMYYYY)
            if (upper.Length >= 8 &&
                DateTime.TryParseExact(upper[..7], "ddMMMyy",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out expiry))
            {
                if (int.TryParse(upper[7..], out strike))
                    return strike > 0;
            }

            if (upper.Length >= 10 &&
                DateTime.TryParseExact(upper[..9], "ddMMMyyyy",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out expiry))
            {
                if (int.TryParse(upper[9..], out strike))
                    return strike > 0;
            }

            return false;
        }
    }
}
