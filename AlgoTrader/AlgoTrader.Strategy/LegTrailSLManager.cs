using System.Collections.Concurrent;
using AlgoTrader.Core.Models;
using AlgoTrader.Core.Enums;

namespace AlgoTrader.Strategy;

public class LegTrailSLManager
{
    // Tracks per-leg state: highest profit seen, current trail SL price, last trail time
    private readonly ConcurrentDictionary<string, LegTrailState> _states = new();
 
    // Call this on every tick for each open leg
    public TrailSLAction EvaluateTick(LegConfig leg, LegPosition position, Tick currentTick, DateTime now)
    {
        var key = $"{position.StrategyId}_{leg.Id}";
        var state = _states.GetOrAdd(key, _ => new LegTrailState { EntryPrice = position.AvgEntryPrice });
        
        decimal currentPnL = CalculateLegPnL(position, currentTick.LTP);
        
        // Update highest profit seen
        if (currentPnL > state.HighestPnL)
            state.HighestPnL = currentPnL;
        
        if (!leg.TrailSLEnabled) return TrailSLAction.NoAction;
        
        // Check X condition: has profit moved X pts/% 
        decimal xThreshold = leg.TrailSLType == TrailSLType.Percentage
            ? position.AvgEntryPrice * leg.TrailSLX / 100
            : leg.TrailSLX;
        
        if (state.HighestPnL < xThreshold) return TrailSLAction.NoAction;
        
        // Check trail frequency
        if (leg.TrailFrequencyType == TrailFrequencyType.TimeBased)
        {
            var minInterval = leg.TrailFrequencyUnit == TrailFrequencyUnit.Seconds
                ? TimeSpan.FromSeconds(leg.TrailFrequencyValue)
                : TimeSpan.FromMinutes(leg.TrailFrequencyValue);
            if (now - state.LastTrailTime < minInterval) return TrailSLAction.NoAction;
        }
        
        // Calculate new SL price
        decimal trailBy = leg.TrailSLType == TrailSLType.Percentage
            ? currentTick.LTP * leg.TrailSLY / 100
            : leg.TrailSLY;
        
        decimal newSLPrice = position.BuySell == BuySell.BUY
            ? currentTick.LTP - trailBy
            : currentTick.LTP + trailBy;
        
        // Only move SL in favourable direction (never widen)
        bool shouldUpdate = position.BuySell == BuySell.BUY
            ? newSLPrice > state.CurrentSLPrice
            : newSLPrice < state.CurrentSLPrice;
        
        if (!shouldUpdate) return TrailSLAction.NoAction;
        
        state.CurrentSLPrice = newSLPrice;
        state.LastTrailTime = now;
        
        return new TrailSLAction { ShouldModify = true, NewSLPrice = newSLPrice };
    }
    
    private decimal CalculateLegPnL(LegPosition pos, decimal currentLTP)
        => pos.BuySell == BuySell.BUY
            ? (currentLTP - pos.AvgEntryPrice) * pos.Qty
            : (pos.AvgEntryPrice - currentLTP) * pos.Qty;
}
 
public class LegTrailState
{
    public decimal EntryPrice { get; set; }
    public decimal HighestPnL { get; set; }
    public decimal CurrentSLPrice { get; set; }
    public DateTime LastTrailTime { get; set; }
}
 
public class TrailSLAction
{
    public bool ShouldModify { get; set; }
    public decimal NewSLPrice { get; set; }
    public static TrailSLAction NoAction => new() { ShouldModify = false };
}
