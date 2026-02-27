using AlgoTrader.Core.Models;
using AlgoTrader.Core.Enums;

namespace AlgoTrader.Strategy;

public class PremiumMatchingValidator
{
    // Called BEFORE entry to validate all leg premiums are within tolerance
    public async Task<bool> ValidatePremiumMatchAsync(
        StrategyConfig strategy, 
        Dictionary<string, decimal> legLTPs)  // legId → current LTP
    {
        if (!strategy.PremiumMatchingEnabled) return true;
        
        var configuredLegs = strategy.Legs
            .Where(l => l.StrikeSelectionMode == StrikeSelectionMode.ByPremium)
            .ToList();
        if (!configuredLegs.Any()) return true;
        
        if (strategy.PremiumMatchMode == PremiumMatchMode.CloseTo)
        {
            // All selected premiums must be within MaxDiffPercent of each other
            var prices = configuredLegs.Select(l => legLTPs.GetValueOrDefault(l.Id.ToString())).ToList();
            if(!prices.Any()) return true;
            decimal avg = prices.Average();
            if(avg == 0) return true;
            return prices.All(p => Math.Abs(p - avg) / avg * 100 <= strategy.PremiumMatchMaxDiffPercent);
        }
        else // Range
        {
            return configuredLegs.All(l =>
            {
                var ltp = legLTPs.GetValueOrDefault(l.Id.ToString());
                return ltp >= strategy.PremiumMatchRangeLow && ltp <= strategy.PremiumMatchRangeHigh;
            });
        }
    }
}
