using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using AlgoTrader.Core.Enums;
using AlgoTrader.Core.Interfaces;
using AlgoTrader.Core.Models;
using Microsoft.Extensions.Logging;

namespace AlgoTrader.Strategy;

/// <summary>
/// G.6 – Auto Square-Off Manager
/// Manages global and per-strategy auto square-off timers.
/// Supports:
///   - Daily auto square-off at configured time (e.g., 3:15 PM)
///   - Expiry-day early square-off at configured time (e.g., 3:20 PM)  
///   - Global market close safety net (3:25 PM)
/// </summary>
public class AutoSquareOffManager : IDisposable
{
    private readonly IStrategyEngine _strategyEngine;
    private readonly ILogger<AutoSquareOffManager> _logger;
    private Timer? _globalTimer;
    private readonly ConcurrentDictionary<string, SquareOffSchedule> _schedules = new();
    private readonly Subject<SquareOffEvent> _squareOffEvents = new();
    private bool _isRunning;

    public IObservable<SquareOffEvent> SquareOffEvents => _squareOffEvents.AsObservable();

    public AutoSquareOffManager(IStrategyEngine strategyEngine, ILogger<AutoSquareOffManager> logger)
    {
        _strategyEngine = strategyEngine;
        _logger = logger;
    }

    /// <summary>Start the auto square-off monitoring loop (runs every 5 seconds).</summary>
    public void Start()
    {
        _isRunning = true;
        _globalTimer = new Timer(async _ => await EvaluateSquareOffsAsync(), null, 
            TimeSpan.Zero, TimeSpan.FromSeconds(5));
        _logger.LogInformation("AutoSquareOffManager started");
    }

    /// <summary>Register a strategy for auto square-off monitoring.</summary>
    public void RegisterStrategy(StrategyConfig config)
    {
        var schedule = new SquareOffSchedule
        {
            StrategyId = config.Id,
            DailyAutoSquareOffEnabled = config.DailyAutoSquareOffEnabled,
            DailySquareOffTime = config.DailySquareOffTime,
            ExpiryDaySquareOffEnabled = config.ExpiryDaySquareOffEnabled,
            ExpiryDaySquareOffTime = config.ExpiryDaySquareOffTime,
            IsPositional = config.IsPositional,
            PositionalDurationType = config.PositionalDurationType,
            DaysBeforeExpiry = config.DaysBeforeExpiry,
            NearestLegExpiry = config.Legs.Any() 
                ? config.Legs.Min(l => l.Expiry) 
                : DateTime.MaxValue
        };
        _schedules[config.Id.ToString()] = schedule;
        _logger.LogDebug("Registered strategy {Id} for auto square-off monitoring", config.Id);
    }

    /// <summary>Remove a strategy from monitoring.</summary>
    public void RemoveStrategy(Guid strategyId)
    {
        _schedules.TryRemove(strategyId.ToString(), out _);
    }

    private async Task EvaluateSquareOffsAsync()
    {
        if (!_isRunning) return;

        try
        {
            var now = DateTime.Now;
            var ist = TimeZoneInfo.ConvertTime(now, 
                TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"));
            var currentTime = ist.TimeOfDay;
            var today = ist.Date;

            // Global market close safety net: 3:25 PM IST
            var globalCutoff = new TimeSpan(15, 25, 0);

            foreach (var kvp in _schedules)
            {
                var schedule = kvp.Value;
                if (schedule.HasBeenSquaredOff) continue;

                bool shouldSquareOff = false;
                string reason = string.Empty;

                // 1. Global cutoff — always exit at 3:25 PM (intraday only)
                if (!schedule.IsPositional && currentTime >= globalCutoff)
                {
                    shouldSquareOff = true;
                    reason = "Global market close safety cutoff (3:25 PM)";
                }

                // 2. Daily auto square-off (user-configured per strategy)
                if (!shouldSquareOff && schedule.DailyAutoSquareOffEnabled && 
                    !schedule.IsPositional && currentTime >= schedule.DailySquareOffTime)
                {
                    shouldSquareOff = true;
                    reason = $"Daily auto square-off at {schedule.DailySquareOffTime}";
                }

                // 3. Expiry day early square-off
                if (!shouldSquareOff && schedule.ExpiryDaySquareOffEnabled)
                {
                    bool isExpiryDay = today == schedule.NearestLegExpiry.Date;
                    if (isExpiryDay && currentTime >= schedule.ExpiryDaySquareOffTime)
                    {
                        shouldSquareOff = true;
                        reason = $"Expiry day square-off at {schedule.ExpiryDaySquareOffTime}";
                    }
                }

                // 4. Positional N-days-before-expiry auto-exit
                if (!shouldSquareOff && schedule.IsPositional && 
                    schedule.PositionalDurationType == PositionalDurationType.NBeforeExpiry)
                {
                    var daysToExpiry = (schedule.NearestLegExpiry.Date - today).Days;
                    if (daysToExpiry <= schedule.DaysBeforeExpiry && 
                        currentTime >= new TimeSpan(15, 15, 0))
                    {
                        shouldSquareOff = true;
                        reason = $"Positional exit: {daysToExpiry} days to expiry (threshold: {schedule.DaysBeforeExpiry})";
                    }
                }

                if (shouldSquareOff)
                {
                    _logger.LogInformation("Auto square-off triggered for strategy {Id}: {Reason}", 
                        schedule.StrategyId, reason);

                    schedule.HasBeenSquaredOff = true;
                    await _strategyEngine.ExitStrategyAsync(schedule.StrategyId, ExitReason.TimeBasedExit);
                    
                    _squareOffEvents.OnNext(new SquareOffEvent(
                        schedule.StrategyId, reason, DateTime.UtcNow));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in AutoSquareOffManager evaluation loop");
        }
    }

    public void Stop()
    {
        _isRunning = false;
        _globalTimer?.Dispose();
        _logger.LogInformation("AutoSquareOffManager stopped");
    }

    public void Dispose()
    {
        Stop();
        _squareOffEvents.Dispose();
    }
}

/// <summary>Internal schedule record for a strategy.</summary>
public class SquareOffSchedule
{
    public Guid StrategyId { get; set; }
    public bool DailyAutoSquareOffEnabled { get; set; }
    public TimeSpan DailySquareOffTime { get; set; }
    public bool ExpiryDaySquareOffEnabled { get; set; }
    public TimeSpan ExpiryDaySquareOffTime { get; set; }
    public bool IsPositional { get; set; }
    public PositionalDurationType PositionalDurationType { get; set; }
    public int DaysBeforeExpiry { get; set; }
    public DateTime NearestLegExpiry { get; set; }
    public bool HasBeenSquaredOff { get; set; }
}

/// <summary>Event emitted when a strategy is auto-squared off.</summary>
public record SquareOffEvent(Guid StrategyId, string Reason, DateTime Timestamp);
