using System.Collections.Concurrent;
using AlgoTrader.Core.Models;

namespace AlgoTrader.Strategy;

public class BreakevenSLManager
{
    private readonly HashSet<string> _alreadyMovedToBreakeven = new();
 
    // Call on every MTM update
    // Returns list of legs whose SL should be moved to entry price (cost)
    public List<LegConfig> EvaluateBreakeven(
        StrategyConfig strategy, 
        decimal currentTotalMTM,
        List<LegPosition> openPositions)
    {
        if (!strategy.MoveSLToCostEnabled) return new List<LegConfig>();
        
        var key = strategy.Id.ToString();
        if (_alreadyMovedToBreakeven.Contains(key)) return new List<LegConfig>();
        
        if (currentTotalMTM >= strategy.MoveSLToCostWhenProfitReaches)
        {
            _alreadyMovedToBreakeven.Add(key);
            // Move SL of all open legs to their entry price
            return strategy.Legs
                .Where(l => openPositions.Any(p => p.LegId == l.Id))
                .ToList();
        }
        return new List<LegConfig>();
    }
    
    public void ResetForStrategy(Guid strategyId) 
        => _alreadyMovedToBreakeven.Remove(strategyId.ToString());
}
