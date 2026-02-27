using AlgoTrader.Core.Enums;
using AlgoTrader.Core.Models;
using Microsoft.Extensions.Logging;

namespace AlgoTrader.Strategy;

/// <summary>
/// G.6 – Positional / Duration Manager
/// Manages carry-forward logic for STBT/BTST and multi-day strategies.
/// Determines whether a strategy should:
///   - Be active today (based on duration rules)
///   - Re-check entry conditions next day after a specific time
///   - Be held overnight (positional) vs forced to exit intraday
/// </summary>
public class PositionalDurationManager
{
    private readonly ILogger<PositionalDurationManager> _logger;

    public PositionalDurationManager(ILogger<PositionalDurationManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Determines if a strategy is eligible to trade today based on its duration settings.
    /// Called at strategy startup / scheduler time.
    /// </summary>
    public DurationCheckResult EvaluateDuration(StrategyConfig strategy, DateTime now)
    {
        var ist = TimeZoneInfo.ConvertTime(now, 
            TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"));
        var today = ist.Date;
        var currentTime = ist.TimeOfDay;

        // Intraday strategies — always active on market days
        if (!strategy.IsPositional && 
            strategy.PositionalDurationType == PositionalDurationType.Intraday)
        {
            return new DurationCheckResult
            {
                CanTradeToday = true,
                ShouldCarryForward = false,
                ShouldExitToday = true,
                Reason = "Intraday strategy — exits by end of day"
            };
        }

        // STBT — Sell Today, Buy Tomorrow
        if (strategy.IsSTBT)
        {
            return EvaluateSTBT(strategy, ist);
        }

        // BTST — Buy Today, Sell Tomorrow
        if (strategy.IsBTST)
        {
            return EvaluateBTST(strategy, ist);
        }

        // Night Position — carry forward to next day
        if (strategy.PositionalDurationType == PositionalDurationType.NightPosition)
        {
            return new DurationCheckResult
            {
                CanTradeToday = true,
                ShouldCarryForward = true,
                ShouldExitToday = false,
                Reason = "Night positional — carries over to next trading day"
            };
        }

        // N Before Expiry — stay in trade until N days before expiry
        if (strategy.PositionalDurationType == PositionalDurationType.NBeforeExpiry)
        {
            var nearestExpiry = strategy.Legs.Any() 
                ? strategy.Legs.Min(l => l.Expiry) 
                : DateTime.MaxValue;
            var daysToExpiry = (nearestExpiry.Date - today).Days;

            if (daysToExpiry <= strategy.DaysBeforeExpiry)
            {
                return new DurationCheckResult
                {
                    CanTradeToday = true,
                    ShouldCarryForward = false,
                    ShouldExitToday = true,
                    Reason = $"N-before-expiry exit: {daysToExpiry} days left (threshold: {strategy.DaysBeforeExpiry})"
                };
            }

            return new DurationCheckResult
            {
                CanTradeToday = true,
                ShouldCarryForward = true,
                ShouldExitToday = false,
                Reason = $"Positional carry: {daysToExpiry} days to expiry (exit at {strategy.DaysBeforeExpiry})"
            };
        }

        // Default: intraday
        return new DurationCheckResult
        {
            CanTradeToday = true,
            ShouldCarryForward = false,
            ShouldExitToday = true,
            Reason = "Default intraday behavior"
        };
    }

    /// <summary>
    /// Checks if a next-day strategy should re-check entry conditions after a configured time.
    /// For STBT/BTST/positional strategies that entered yesterday and need to re-evaluate today.
    /// </summary>
    public bool ShouldReCheckToday(StrategyConfig strategy, DateTime now)
    {
        if (!strategy.CheckConditionNextDayAfter.HasValue) return true;

        var ist = TimeZoneInfo.ConvertTime(now,
            TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"));
        
        return ist.TimeOfDay >= strategy.CheckConditionNextDayAfter.Value;
    }

    private DurationCheckResult EvaluateSTBT(StrategyConfig strategy, DateTime ist)
    {
        var currentTime = ist.TimeOfDay;
        var marketOpen = new TimeSpan(9, 15, 0);
        var marketClose = new TimeSpan(15, 30, 0);

        // STBT: Sell intraday options/futures today, buy them back tomorrow morning
        // Day 1 (today): SELL at entry time
        // Day 2 (tomorrow): BUY back after market open + optional delay
        
        bool isAfterMarketOpen = currentTime >= marketOpen;
        
        return new DurationCheckResult
        {
            CanTradeToday = isAfterMarketOpen,
            ShouldCarryForward = true,
            ShouldExitToday = false,
            ExitNextDayAfter = strategy.CheckConditionNextDayAfter ?? marketOpen,
            Reason = "STBT — sells today, buys back tomorrow"
        };
    }

    private DurationCheckResult EvaluateBTST(StrategyConfig strategy, DateTime ist)
    {
        var currentTime = ist.TimeOfDay;
        var marketOpen = new TimeSpan(9, 15, 0);

        // BTST: Buy today, sell tomorrow
        // Day 1: BUY at entry time
        // Day 2: SELL after market open + optional delay
        
        return new DurationCheckResult
        {
            CanTradeToday = currentTime >= marketOpen,
            ShouldCarryForward = true,
            ShouldExitToday = false,
            ExitNextDayAfter = strategy.CheckConditionNextDayAfter ?? marketOpen,
            Reason = "BTST — buys today, sells tomorrow"
        };
    }
}

/// <summary>Result of duration evaluation for a strategy.</summary>
public class DurationCheckResult
{
    /// <summary>Whether the strategy is allowed to trade today.</summary>
    public bool CanTradeToday { get; set; }

    /// <summary>Whether the position should be held overnight.</summary>
    public bool ShouldCarryForward { get; set; }

    /// <summary>Whether the strategy must exit before market close today.</summary>
    public bool ShouldExitToday { get; set; }

    /// <summary>Time after which to exit on the next day (for STBT/BTST).</summary>
    public TimeSpan? ExitNextDayAfter { get; set; }

    /// <summary>Human-readable reason for the decision.</summary>
    public string Reason { get; set; } = string.Empty;
}
