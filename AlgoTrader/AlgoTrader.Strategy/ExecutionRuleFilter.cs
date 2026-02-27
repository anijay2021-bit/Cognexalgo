using System.Collections.Generic;
using System.Linq;
using AlgoTrader.Core.Models;
using AlgoTrader.Core.Enums;

namespace AlgoTrader.Strategy;

public static class ExecutionRuleFilter
{
    // Apply execution rule BEFORE placing orders — filters/transforms leg list
    public static List<LegConfig> ApplyRule(
        List<LegConfig> legs, ExecutionRule rule)
    {
        return rule switch
        {
            ExecutionRule.LongOnly =>
                legs.Where(l => l.BuySell == BuySell.BUY).ToList(),
            
            ExecutionRule.ShortOnly =>
                legs.Where(l => l.BuySell == BuySell.SELL).ToList(),
            
            ExecutionRule.LongAndShort =>
                legs.ToList(), // all legs as-is
            
            ExecutionRule.Stop =>
                new List<LegConfig>(), // empty = place no orders
            
            ExecutionRule.Reversal =>
                legs.Select(l => l with {
                    BuySell = l.BuySell == BuySell.BUY ? BuySell.SELL : BuySell.BUY
                }).ToList(),
            
            _ => legs.ToList()
        };
    }
    
    public static bool CanEnter(ExecutionRule rule)
        => rule != ExecutionRule.Stop;
}
