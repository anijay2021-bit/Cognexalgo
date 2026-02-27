using System;

namespace Cognexalgo.Core.Models
{
    /// <summary>
    /// Represents a single option in the option chain with real-time data
    /// </summary>
    public class OptionChainItem
    {
        /// <summary>
        /// Strike price (e.g., 23500, 23550, 23600)
        /// </summary>
        public int Strike { get; set; }

        /// <summary>
        /// Option type: "CE" (Call) or "PE" (Put)
        /// </summary>
        public string OptionType { get; set; }

        /// <summary>
        /// Last Traded Price (real-time premium)
        /// </summary>
        public double LTP { get; set; }

        /// <summary>
        /// Trading symbol (e.g., "NIFTY13FEB2624500CE")
        /// </summary>
        public string Symbol { get; set; }

        /// <summary>
        /// Angel One symbol token
        /// </summary>
        public string Token { get; set; }

        /// <summary>
        /// Lot size for this instrument
        /// </summary>
        public int LotSize { get; set; }

        /// <summary>
        /// Checks if this is a Call option
        /// </summary>
        public bool IsCall => OptionType == "CE";

        /// <summary>
        /// Checks if this is a Put option
        /// </summary>
        public bool IsPut => OptionType == "PE";

        // Expiry tracking — populated by BuildOptionChainAsync
        public DateTime ExpiryDate { get; set; }

        /// <summary>Calendar days to expiry (min 1 to avoid div-by-zero in Black-Scholes).</summary>
        public int DaysToExpiry => Math.Max(1, (ExpiryDate.Date - DateTime.Today).Days);

        // [Phase 7] Greeks — computed from market-implied IV via GreeksService
        public double IV    { get; set; }
        public double Delta { get; set; }
        public double Theta { get; set; }
        public double Vega  { get; set; }
        public double Gamma { get; set; }

        public override string ToString()
        {
            return $"{Strike} {OptionType} @ ₹{LTP:N2}";
        }
    }
}
