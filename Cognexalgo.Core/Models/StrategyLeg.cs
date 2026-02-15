using System;
using System.Collections.Generic;
using System.Linq;

namespace Cognexalgo.Core.Models
{
    public enum StrikeSelectionMode
    {
        ATMPoint,
        ATMPercent,
        StraddleWidth,
        ClosestPremium
    }

    public enum OptionType
    {
        Call,
        Put
    }

    public enum ActionType
    {
        Buy,
        Sell
    }

    public class StrategyLeg
    {
        // Strike Selection Mode
        public StrikeSelectionMode Mode { get; set; }

        // ATM Point Mode
        public string ATMOffset { get; set; } // "ATM-100", "ATM-50", "ATM", "ATM+50", etc.

        // ATM Percent Mode
        public double ATMPercentOffset { get; set; } // 0.5, 1.0, 1.5, etc. (as percentage)

        // Straddle Width Mode
        public double StraddleMultiplier { get; set; } // 0.5x, 1x, 2x, etc.

        // Closest Premium Mode
        public double TargetPremium { get; set; }
        public string PremiumOperator { get; set; } // "~", ">=", "<="
        public bool WaitForMatch { get; set; } = false;

        // Common Properties
        public string Index { get; set; } // "NIFTY", "BANKNIFTY"
        public OptionType OptionType { get; set; }
        public ActionType Action { get; set; }
        public int TotalLots { get; set; }
        public string ProductType { get; set; } // "MIS", "NRML"
        public string ExpiryType { get; set; } // "Weekly", "Monthly"
        public int ExpiryOffset { get; set; } = 0; // 0 = Current, 1 = Next, 2 = Next+1


        // Execution Details
        public string Status { get; set; } = "PENDING"; // PENDING, OPEN, EXITED, SQOFF
        public string OrderId { get; set; }
        public string StraddlePairId { get; set; } // Group ID for Straddle Legs
        public int MaxReEntry { get; set; } = 0;   // Number of allowed adjustments
        public int CurrentReEntry { get; set; } = 0;
        
        public double EntryPrice { get; set; }
        public double ExitPrice { get; set; }
        public double EntryIndexLtp { get; set; } // Underlying Index Price at Entry
        public DateTime? EntryTime { get; set; }
        public DateTime? ExitTime { get; set; }
        public double Ltp { get; set; } // Real-time LTP for monitoring

        // Risk Management (Per Leg)
        public double StopLossPrice { get; set; }
        public double TargetPrice { get; set; }
        public double TrailingSL { get; set; }
        
        // Calculated at execution (populated by GetTargetStrike)
        public int CalculatedStrike { get; set; }
        public string SymbolToken { get; set; }
        public double SelectedPremium { get; set; }

        /// <summary>
        /// Calculates the target strike based on the selected mode
        /// </summary>
        public int GetTargetStrike(double spotPrice, List<OptionChainItem> chain)
        {
            // Null check - return 0 for Strategy Invalid state
            if (chain == null || chain.Count == 0)
            {
                return 0; // Strategy Invalid - no option chain available
            }

            int atmStrike = GetATMStrike(spotPrice, chain);
            
            // Check if ATM strike was found
            if (atmStrike == 0)
            {
                return 0; // Strategy Invalid - no ATM strike found
            }

            switch (Mode)
            {
                case StrikeSelectionMode.ATMPoint:
                    return CalculateATMPointStrike(atmStrike);

                case StrikeSelectionMode.ATMPercent:
                    return CalculateATMPercentStrike(spotPrice, chain);

                case StrikeSelectionMode.StraddleWidth:
                    return CalculateStraddleWidthStrike(atmStrike, chain);

                case StrikeSelectionMode.ClosestPremium:
                    int closestStrike = CalculateClosestPremiumStrike(chain);
                    if (closestStrike == 0)
                    {
                        return 0; // Strategy Invalid - no matching premium found
                    }
                    return closestStrike;

                default:
                    return atmStrike;
            }
        }

        private int GetATMStrike(double spotPrice, List<OptionChainItem> chain)
        {
            // Find the strike closest to spot price
            // Group by strike and take the first one
            var strikes = chain.Select(x => x.Strike).Distinct().ToList();
            if (strikes.Count == 0)
                return 0; // No strikes available
            return strikes.OrderBy(x => Math.Abs(x - spotPrice)).FirstOrDefault();
        }

        private int CalculateATMPointStrike(int atmStrike)
        {
            // Parse offset like "ATM-100", "ATM+50", "ATM"
            if (ATMOffset == "ATM")
                return atmStrike;

            string offsetStr = ATMOffset.Replace("ATM", "").Trim();
            if (int.TryParse(offsetStr, out int offset))
            {
                return atmStrike + offset;
            }

            return atmStrike;
        }

        private int CalculateATMPercentStrike(double spotPrice, List<OptionChainItem> chain)
        {
            // Formula: Spot Price + (Spot Price × Percent)
            double targetPrice = spotPrice + (spotPrice * (ATMPercentOffset / 100.0));

            // Round to nearest tradable strike
            return chain.OrderBy(x => Math.Abs(x.Strike - targetPrice)).First().Strike;
        }

        private int CalculateStraddleWidthStrike(int atmStrike, List<OptionChainItem> chain)
        {
            // Find ATM Call and Put
            var atmCall = chain.FirstOrDefault(x => x.Strike == atmStrike && x.OptionType == "CE");
            var atmPut = chain.FirstOrDefault(x => x.Strike == atmStrike && x.OptionType == "PE");
            
            if (atmCall == null || atmPut == null)
                return atmStrike;

            // Combined ATM Premium = Call LTP + Put LTP
            double combinedPremium = atmCall.LTP + atmPut.LTP;

            // Target Price = ATM Strike ± (Multiplier × Combined Premium)
            double targetPrice = atmStrike;
            if (OptionType == OptionType.Call)
                targetPrice = atmStrike + (StraddleMultiplier * combinedPremium);
            else
                targetPrice = atmStrike - (StraddleMultiplier * combinedPremium);

            // Find strike closest to target price
            var strikes = chain.Select(x => x.Strike).Distinct().ToList();
            if (strikes.Count == 0)
            {
                // No strikes found - return 0
                return 0;
            }
            return strikes.OrderBy(x => Math.Abs(x - targetPrice)).FirstOrDefault();
        }

        private int CalculateClosestPremiumStrike(List<OptionChainItem> chain)
        {
            // Default: Wait for Match if no premium found?
            // Handled by returning 0
            
            var targetOptionType = OptionType == OptionType.Call ? "CE" : "PE";
            var relevantOptions = chain.Where(x => x.OptionType == targetOptionType).ToList();
            
            if (!relevantOptions.Any()) return 0;

            // 1. Find Closest Match
            var bestMatch = relevantOptions.OrderBy(x => Math.Abs(x.LTP - TargetPremium)).FirstOrDefault();
            
            if (bestMatch == null) return 0;
            
            // 2. Check Tolerance (Optional)
            // If we strictly want to match "within 5%", we can add a check here.
            // For now, returning the *abs* closest is standard behavior for "Closest Premium".
            
            SelectedPremium = bestMatch.LTP;
            return bestMatch.Strike;
        }

        public string GetStrikeDisplay()
        {
            switch (Mode)
            {
                case StrikeSelectionMode.ATMPoint:
                    return ATMOffset;
                case StrikeSelectionMode.ATMPercent:
                    return $"ATM{(ATMPercentOffset >= 0 ? "+" : "")}{ATMPercentOffset}%";
                case StrikeSelectionMode.StraddleWidth:
                    return $"SW {StraddleMultiplier}x";
                case StrikeSelectionMode.ClosestPremium:
                    return $"CP {PremiumOperator} {TargetPremium}";
                default:
                    return "ATM";
            }
        }
    }
}
