using System.Collections.Concurrent;
using AlgoTrader.Core.Models;
using AlgoTrader.Core.Enums;
using Microsoft.Extensions.Logging;

namespace AlgoTrader.Strategy;

public class AdvancedReEntryManager
{
    private readonly ConcurrentDictionary<string, ReEntryState> _states = new();
    private readonly ILogger<AdvancedReEntryManager> _logger;
    // Assuming EntryExecutor is passed or handled via delegate/interface in real implementation
    // For now we will assume the caller handles the actual entry to keep it decouple from StrategyEngine internals if possible,
    // or we define an interface IEntryExecutor if needed. The prompt uses EntryExecutor.
    // We will inject StrategyEngine or a similar interface if EntryExecutor is not a distinct service.
 
    public AdvancedReEntryManager(ILogger<AdvancedReEntryManager> logger)
    {
        _logger = logger;
    }

    public bool ShouldLegReEnter(
        LegConfig leg, StrategyConfig strategy,
        LegPosition exitedPosition,
        Tick currentTick, DateTime now)
    {
        var key = $"{strategy.Id}_{leg.Id}";
        var state = _states.GetOrAdd(key, _ => new ReEntryState());
        
        if (!leg.ReEntryEnabled) return false;
        if (state.ReEntryCount >= leg.MaxLegReEntries) return false;
        
        // Time window check
        if (leg.LegReEntryOnlyAfter.HasValue && now.TimeOfDay < leg.LegReEntryOnlyAfter.Value) return false;
        if (leg.LegReEntryOnlyBefore.HasValue && now.TimeOfDay > leg.LegReEntryOnlyBefore.Value) return false;
        
        bool shouldReEnter = leg.LegReEntryType switch
        {
            LegReEntryType.ReEnterImmediately => true,
            LegReEntryType.ReverseAndReEnterImmediately => true,
            LegReEntryType.ReEnterAtCost =>
                // Wait until LTP returns to original entry cost
                leg.LegReEntryMethod == LegReEntryMethod.LTP
                    ? Math.Abs(currentTick.LTP - exitedPosition.AvgEntryPrice) <= 1.0m
                    : false, // candle close handled separately
            LegReEntryType.ReverseAndReEnterAtCost =>
                Math.Abs(currentTick.LTP - exitedPosition.AvgEntryPrice) <= 1.0m,
            _ => false
        };
        
        if (!shouldReEnter) return false;
        
        return true;
    }
    
    // Returns the re-entry leg config (flipped if reversal)
    public LegConfig ProcessLegReEntry(LegConfig leg, StrategyConfig strategy)
    {
        var key = $"{strategy.Id}_{leg.Id}";
        var state = _states.GetOrAdd(key, _ => new ReEntryState());

        bool isReversal = leg.LegReEntryType is 
            LegReEntryType.ReverseAndReEnterAtCost or 
            LegReEntryType.ReverseAndReEnterImmediately;
        
        var reEntryLeg = leg with {
            BuySell = isReversal 
                ? (leg.BuySell == BuySell.BUY ? BuySell.SELL : BuySell.BUY)
                : leg.BuySell
        };
        
        state.ReEntryCount++;
        _logger.LogInformation("Re-entry #{ReEntryCount} for leg {LegName}", state.ReEntryCount, leg.LegName);
        
        return reEntryLeg;
    }
    
    public bool ShouldCombinedReEnter(StrategyConfig strategy)
    {
        if (!strategy.CombinedReEntryEnabled) return false;
        var key = strategy.Id.ToString();
        var state = _states.GetOrAdd(key, _ => new ReEntryState());
        if (state.ReEntryCount >= strategy.MaxCombinedReEntries) return false;
        return true;
    }

    public StrategyConfig ProcessCombinedReEntry(StrategyConfig strategy)
    {
        var key = strategy.Id.ToString();
        var state = _states.GetOrAdd(key, _ => new ReEntryState());
        
        bool reverse = strategy.CombinedReEntryType == CombinedReEntryType.ReverseAndReEnterImmediately;
        state.ReEntryCount++;
        
        // Clone strategy with flipped legs if reversal
        var reEntryStrategy = reverse ? FlipAllLegs(strategy) : strategy;
        return reEntryStrategy;
    }
    
    private StrategyConfig FlipAllLegs(StrategyConfig s)
    {
        // Deep clone and flip every leg's BuySell
        var clone = s with { Legs = s.Legs.Select(l => l with {
            BuySell = l.BuySell == BuySell.BUY ? BuySell.SELL : BuySell.BUY
        }).ToList() };
        return clone;
    }
}
 
public class ReEntryState { public int ReEntryCount { get; set; } }
