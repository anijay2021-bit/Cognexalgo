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
        ClosestPremium,
        ByDelta          // F7: select strike whose delta is closest to TargetDelta
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
        public string PremiumOperator { get; set; } = "~"; // "~" closest | ">=" at-least | "<=" at-most
        public bool WaitForMatch { get; set; } = false;
        /// <summary>
        /// Max acceptable deviation from TargetPremium (in ₹) when WaitForMatch is true.
        /// Entry is deferred until a strike exists within this band.
        /// 0 = no tolerance guard (always enter with closest).
        /// </summary>
        public double PremiumTolerance { get; set; } = 10.0;

        // F7: Delta Mode
        /// <summary>Target delta magnitude (0.0–1.0). Used when Mode == ByDelta.</summary>
        public double TargetDelta { get; set; } = 0.3;

        // Common Properties
        public string Index { get; set; } // "NIFTY", "BANKNIFTY"
        public OptionType OptionType { get; set; }
        public ActionType Action { get; set; }
        public int TotalLots { get; set; }
        public int LotSize { get; set; } = 1; // Resolved from instrument master at entry time
        public string ProductType { get; set; } // "MIS", "NRML"
        public string ExpiryType { get; set; } // "Weekly", "Monthly"
        public int ExpiryOffset { get; set; } = 0; // 0 = Current, 1 = Next, 2 = Next+1


        // Execution Details
        public string Status { get; set; } = "PENDING"; // PENDING, OPEN, EXITED, SQOFF
        public string ExitReason { get; set; } = ""; // "SL", "TARGET", "MANUAL", "RMS"
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

        /// <summary>SL as % of entry premium. 25 = exit when premium moves 25% against position. Overrides StopLossPrice at entry if > 0.</summary>
        public double StopLossPercent { get; set; } = 0;
        /// <summary>Target as % of entry premium. 50 = exit when 50% profit is locked. Overrides TargetPrice at entry if > 0.</summary>
        public double TargetPercent { get; set; } = 0;

        // F6: SL-M exit order
        /// <summary>Use SL-M (stop-loss market) order type for exits in live mode.</summary>
        public bool UseSLMOnExit { get; set; } = false;

        // F2: Indicator entry conditions — ALL must be true before entry
        public List<IndicatorCondition> EntryConditions { get; set; } = new();

        // F8: Adjustment legs
        /// <summary>True = this leg is inactive at start; activates when its trigger fires.</summary>
        public bool IsAdjustmentLeg { get; set; } = false;
        /// <summary>"None" | "ParentLegPnL" | "UnderlyingMove"</summary>
        public string AdjustmentTrigger { get; set; } = "None";
        /// <summary>₹ loss threshold (ParentLegPnL) or point move (UnderlyingMove) that activates this leg.</summary>
        public decimal AdjustmentTriggerValue { get; set; } = 0;
        /// <summary>Zero-based index of the parent leg in the Legs list. -1 = no parent.</summary>
        public int ParentLegIndex { get; set; } = -1;
        
        // Calculated at execution (populated by GetTargetStrike)
        public int CalculatedStrike { get; set; }
        public string SymbolToken { get; set; }
        public double SelectedPremium { get; set; }
        /// <summary>Angel One option trading symbol e.g. "NIFTY25MAR21000CE". Set at entry from OptionChainItem.Symbol.</summary>
        public string TradingSymbol { get; set; } = string.Empty;

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

                case StrikeSelectionMode.ByDelta:
                    return GetDeltaStrike(chain);

                default:
                    return atmStrike;
            }
        }

        /// <summary>F7: Find strike whose absolute delta is closest to TargetDelta.</summary>
        private int GetDeltaStrike(List<OptionChainItem> chain)
        {
            var targetType = OptionType == OptionType.Call ? "CE" : "PE";
            var filtered = chain.Where(x => x.OptionType == targetType && x.Delta != 0).ToList();
            if (!filtered.Any()) return 0;

            return filtered
                .OrderBy(x => Math.Abs(Math.Abs((double)x.Delta) - Math.Abs(TargetDelta)))
                .First().Strike;
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
            var targetType = OptionType == OptionType.Call ? "CE" : "PE";
            var candidates = chain.Where(x => x.OptionType == targetType && x.LTP > 0).ToList();
            if (!candidates.Any()) return 0;

            // ── 1. Apply operator filter ──────────────────────────────────────────
            IEnumerable<OptionChainItem> filtered = PremiumOperator switch
            {
                ">=" => candidates.Where(x => x.LTP >= TargetPremium),  // at-least: entry premium >= target
                "<=" => candidates.Where(x => x.LTP <= TargetPremium),  // at-most:  entry premium <= target
                _    => candidates                                        // "~" closest: no pre-filter
            };

            // If operator filtering left nothing, fall back to full list
            if (!filtered.Any()) filtered = candidates;

            // ── 2. Pick candidate with LTP closest to TargetPremium ───────────────
            var best = filtered.OrderBy(x => Math.Abs(x.LTP - TargetPremium)).First();

            // ── 3. WaitForMatch guard: defer entry if best is outside tolerance ───
            if (WaitForMatch && PremiumTolerance > 0 &&
                Math.Abs(best.LTP - TargetPremium) > PremiumTolerance)
            {
                return 0; // no match within band — skip this tick, try next
            }

            SelectedPremium = best.LTP;
            return best.Strike;
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
                case StrikeSelectionMode.ByDelta:
                    return $"Δ{TargetDelta:F2}";
                default:
                    return "ATM";
            }
        }
    }
}
