using AlgoTrader.Core.Models;
using AlgoTrader.Core.Enums;

namespace AlgoTrader.Strategy;

public class WaitAndTradeEvaluator
{
    // Call this AFTER strategy's normal entry time is reached but BEFORE placing orders
    // Returns true when the wait condition is satisfied and entry should proceed
    public bool IsEntryConditionSatisfied(
        StrategyConfig strategy, 
        decimal currentUnderlyingLTP,
        decimal underlyingLTPAtStartTime)   // captured at strategy start time
    {
        if (!strategy.WaitAndTradeEnabled || 
            strategy.WaitAndTradeType == WaitAndTradeType.Immediate)
            return true;
        
        if (underlyingLTPAtStartTime == 0) return true; // prevent divide by zero
        
        decimal move = currentUnderlyingLTP - underlyingLTPAtStartTime;
        decimal movePct = move / underlyingLTPAtStartTime * 100;
        
        return strategy.WaitAndTradeType switch
        {
            WaitAndTradeType.PercentUp   => movePct >= strategy.WaitAndTradeValue,
            WaitAndTradeType.PercentDown => movePct <= -strategy.WaitAndTradeValue,
            WaitAndTradeType.PointsUp    => move >= strategy.WaitAndTradeValue,
            WaitAndTradeType.PointsDown  => move <= -strategy.WaitAndTradeValue,
            _ => true
        };
    }
}
