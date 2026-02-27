using System.Collections.Concurrent;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using AlgoTrader.Core.Enums;
using AlgoTrader.Core.Interfaces;
using AlgoTrader.Core.Models;
using AlgoTrader.MarketData;
using AlgoTrader.OMS;
using Microsoft.Extensions.Logging;

namespace AlgoTrader.Strategy;

/// <summary>Core strategy engine — manages lifecycle of all active strategies.</summary>
public class StrategyEngine : IStrategyEngine
{
    private readonly Dictionary<string, StrategyInstance> _activeStrategies = new();
    private readonly IMarketDataService _marketData;
    private readonly IOrderManager _orderManager;
    private readonly ExecutionEngine _executionEngine;
    private readonly IRiskManager _riskManager;
    private readonly INotificationService _notifier;
    private readonly ILogger<StrategyEngine> _logger;
    private readonly AdvancedReEntryManager _reEntryManager;
    private readonly StrategyEventBus _eventBus;
    private readonly AutoSquareOffManager _autoSquareOff;
    private readonly PositionalDurationManager _durationManager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly PositionTracker? _positionTracker;

    // Optional services — injected when available
    private readonly WaitAndTradeEvaluator? _waitAndTrade;
    private readonly PremiumStrikeSelector? _premiumSelector;
    private readonly PremiumMatchingValidator? _premiumValidator;
    private readonly BreakevenSLManager? _breakevenManager;
    private readonly IndicatorConditionEvaluator? _indicatorEvaluator;

    public StrategyEventBus EventBus => _eventBus;
    public AutoSquareOffManager AutoSquareOff => _autoSquareOff;

    public StrategyEngine(
        IMarketDataService marketData,
        IOrderManager orderManager,
        ExecutionEngine executionEngine,
        IRiskManager riskManager,
        INotificationService notifier,
        ILoggerFactory loggerFactory,
        AdvancedReEntryManager reEntryManager,
        WaitAndTradeEvaluator? waitAndTrade = null,
        PremiumStrikeSelector? premiumSelector = null,
        PremiumMatchingValidator? premiumValidator = null,
        BreakevenSLManager? breakevenManager = null,
        IndicatorConditionEvaluator? indicatorEvaluator = null,
        PositionTracker? positionTracker = null)
    {
        _marketData = marketData;
        _orderManager = orderManager;
        _executionEngine = executionEngine;
        _riskManager = riskManager;
        _notifier = notifier;
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<StrategyEngine>();
        _reEntryManager = reEntryManager;
        _eventBus = new StrategyEventBus();
        _autoSquareOff = new AutoSquareOffManager(this, _loggerFactory.CreateLogger<AutoSquareOffManager>());
        _durationManager = new PositionalDurationManager(_loggerFactory.CreateLogger<PositionalDurationManager>());
        _positionTracker = positionTracker;

        // Optional G.2 / G.3 / G.6 / G.10 services
        _waitAndTrade = waitAndTrade ?? new WaitAndTradeEvaluator();
        _premiumSelector = premiumSelector;
        _premiumValidator = premiumValidator ?? new PremiumMatchingValidator();
        _breakevenManager = breakevenManager ?? new BreakevenSLManager();
        _indicatorEvaluator = indicatorEvaluator;
    }

    public Task StartAsync()
    {
        _autoSquareOff.Start();

        // ── Wire PositionTracker → RiskManager ───────────────────────────────
        // PositionTracker publishes live per-strategy MTM; RiskManager subscribes
        // and emits RiskAlerts when MaxLoss / MaxProfit thresholds are breached.
        if (_positionTracker != null)
        {
            _riskManager.MonitorMTM(_positionTracker.MTMUpdates);

            // Force-exit the affected strategy the moment a risk limit fires
            _riskManager.RiskAlerts
                .Subscribe(async evt =>
                {
                    _logger.LogWarning("RiskAlert [{Type}] on strategy {Id} — forcing exit", evt.Type, evt.StrategyId);
                    if (_activeStrategies.TryGetValue(evt.StrategyId, out var instance))
                        await instance.ForceExitAsync(ExitReason.Manual);
                });
        }

        _logger.LogInformation("StrategyEngine started with {Count} strategies", _activeStrategies.Count);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        foreach (var kvp in _activeStrategies)
            kvp.Value.Stop();
        _activeStrategies.Clear();
        _autoSquareOff.Stop();
        _logger.LogInformation("StrategyEngine stopped — all strategies removed");
        return Task.CompletedTask;
    }

    public async Task RegisterStrategyAsync(StrategyConfig config)
    {
        var id = config.Id.ToString();
        if (_activeStrategies.ContainsKey(id))
        {
            _logger.LogWarning("Strategy {Id} already registered", id);
            return;
        }

        // G.6: Duration check — is this strategy eligible to trade today?
        var durationResult = _durationManager.EvaluateDuration(config, DateTime.Now);
        if (!durationResult.CanTradeToday)
        {
            _logger.LogInformation("Strategy {Name} skipped today: {Reason}", config.Name, durationResult.Reason);
            return;
        }

        var instance = new StrategyInstance(
            config, _marketData, _orderManager, _executionEngine, 
            _notifier, _eventBus, _loggerFactory, _reEntryManager,
            _waitAndTrade, _premiumSelector, _premiumValidator,
            _breakevenManager, _indicatorEvaluator, _durationManager,
            _positionTracker);
        _activeStrategies[id] = instance;
        await instance.StartAsync();

        // G.6: Register for auto square-off monitoring
        _autoSquareOff.RegisterStrategy(config);

        _logger.LogInformation("Strategy registered: {Name} ({Id}) — Duration: {Reason}", 
            config.Name, id, durationResult.Reason);
    }

    public Task RemoveStrategyAsync(string strategyId)
    {
        if (_activeStrategies.TryGetValue(strategyId, out var instance))
        {
            instance.Stop();
            _activeStrategies.Remove(strategyId);
            _autoSquareOff.RemoveStrategy(Guid.Parse(strategyId));
            _logger.LogInformation("Strategy removed: {Id}", strategyId);
        }
        return Task.CompletedTask;
    }

    public async Task StartStrategyAsync(Guid strategyId)
    {
        var id = strategyId.ToString();
        if (_activeStrategies.TryGetValue(id, out var instance))
        {
            await instance.StartAsync();
        }
    }

    public Task StopStrategyAsync(Guid strategyId)
    {
        var id = strategyId.ToString();
        if (_activeStrategies.TryGetValue(id, out var instance))
        {
            instance.Stop();
        }
        return Task.CompletedTask;
    }

    public async Task ExitStrategyAsync(Guid strategyId, ExitReason reason)
    {
        var id = strategyId.ToString();
        if (_activeStrategies.TryGetValue(id, out var instance))
        {
            await instance.ForceExitAsync(reason);
        }
    }

    public async Task SquareOffAllAsync()
    {
        _logger.LogInformation("Square Off All triggered for {Count} strategies", _activeStrategies.Count);
        var strategyIds = _activeStrategies.Keys.ToList();
        foreach (var id in strategyIds)
        {
            await ExitStrategyAsync(Guid.Parse(id), ExitReason.Manual);
        }
    }
}

/// <summary>Single running strategy instance — monitors ticks and manages entry/exit lifecycle.</summary>
public class StrategyInstance : IDisposable
{
    private readonly StrategyConfig _config;
    private readonly IMarketDataService _marketData;
    private readonly IOrderManager _orderManager;
    private readonly ExecutionEngine _executionEngine;
    private readonly INotificationService _notifier;
    private readonly StrategyEventBus _eventBus;
    private readonly ILogger<StrategyInstance> _logger;
    private readonly CompositeDisposable _subscriptions = new();
    private readonly EntryEvaluator _entryEvaluator;
    private readonly ExitEvaluator _exitEvaluator;
    private readonly AdvancedReEntryManager _reEntryManager;
    private CancellationTokenSource _cts = new();

    // G.2 / G.3 / G.6 / G.10 services
    private readonly WaitAndTradeEvaluator? _waitAndTrade;
    private readonly PremiumStrikeSelector? _premiumSelector;
    private readonly PremiumMatchingValidator? _premiumValidator;
    private readonly BreakevenSLManager? _breakevenManager;
    private readonly IndicatorConditionEvaluator? _indicatorEvaluator;
    private readonly PositionalDurationManager? _durationManager;
    private readonly PositionTracker? _positionTracker;

    // G.3: Wait & Trade state
    private decimal _underlyingLTPAtStartTime;
    private bool _waitConditionSatisfied;

    // G.6: Move SL to Cost state  
    private readonly List<LegPosition> _openLegPositions = new();

    public StrategyConfig Config => _config;

    public StrategyInstance(
        StrategyConfig config,
        IMarketDataService marketData,
        IOrderManager orderManager,
        ExecutionEngine executionEngine,
        INotificationService notifier,
        StrategyEventBus eventBus,
        ILoggerFactory loggerFactory,
        AdvancedReEntryManager reEntryManager,
        WaitAndTradeEvaluator? waitAndTrade = null,
        PremiumStrikeSelector? premiumSelector = null,
        PremiumMatchingValidator? premiumValidator = null,
        BreakevenSLManager? breakevenManager = null,
        IndicatorConditionEvaluator? indicatorEvaluator = null,
        PositionalDurationManager? durationManager = null,
        PositionTracker? positionTracker = null)
    {
        _config = config;
        _marketData = marketData;
        _orderManager = orderManager;
        _executionEngine = executionEngine;
        _notifier = notifier;
        _eventBus = eventBus;
        _logger = loggerFactory.CreateLogger<StrategyInstance>();
        _reEntryManager = reEntryManager;
        _waitAndTrade = waitAndTrade;
        _premiumSelector = premiumSelector;
        _premiumValidator = premiumValidator;
        _breakevenManager = breakevenManager;
        _indicatorEvaluator = indicatorEvaluator;
        _durationManager = durationManager;
        _positionTracker = positionTracker;

        _entryEvaluator = new EntryEvaluator(loggerFactory.CreateLogger<EntryEvaluator>(), indicatorEvaluator);
        _exitEvaluator = new ExitEvaluator(loggerFactory.CreateLogger<ExitEvaluator>(), indicatorEvaluator);
    }

    public async Task StartAsync()
    {
        _config.State = StrategyState.WAITING_ENTRY;
        _underlyingLTPAtStartTime = 0;
        _waitConditionSatisfied = false;

        // Subscribe to ticks for tokens in this strategy's legs
        var tokens = _config.Legs.Select(l => (l.Exchange, l.Token)).Distinct().ToList();
        await _marketData.SubscribeAsync(tokens, SubscriptionMode.LTP);

        // Monitor ticks
        var sub = _marketData.TickStream
            .Where(t => _config.Legs.Any(l => l.Token == t.Token))
            .Subscribe(OnTick);
        _subscriptions.Add(sub);

        // Monitor leg-level exits via ExecutionEngine
        var execSub = _executionEngine.ExecutionUpdates
            .Where(u => u.Tag.StartsWith($"SL_{_config.Id}") || u.Tag.StartsWith($"TGT_{_config.Id}"))
            .Where(u => u.Status == OrderStatus.COMPLETE)
            .Subscribe(OnLegExit);
        _subscriptions.Add(execSub);

        _logger.LogInformation("Strategy {Name} armed - waiting for entry", _config.Name);
    }

    private async void OnTick(Tick tick)
    {
        try
        {
            // ─── Real-time MTM Update ───
            if (_config.State == StrategyState.ENTERED && _positionTracker != null)
            {
                var matchingLegs = _openLegPositions.Where(l => l.Token == tick.Token).ToList();
                if (matchingLegs.Count > 0)
                {
                    _positionTracker.UpdateMTM(_config.Id.ToString(), tick, matchingLegs);
                }
            }
            // ── Check for pending "At Cost" leg re-entries ──
            var legsToReEnter = _pendingLegReEntries.Values
                .Where(p => p.Leg.Token == tick.Token)
                .ToList();

            foreach (var pending in legsToReEnter)
            {
                if (_reEntryManager.ShouldLegReEnter(pending.Leg, _config, pending.ExitPos, tick, DateTime.Now))
                {
                    _pendingLegReEntries.Remove(pending.Leg.Id);
                    ExecuteLegReEntry(pending.Leg);
                }
            }

            // ── G.6: Move SL to Cost (Breakeven) check ──
            if (_config.State == StrategyState.ENTERED && _breakevenManager != null)
            {
                // Calculate current total MTM (simplified)
                decimal totalMTM = 0;
                foreach (var pos in _openLegPositions)
                {
                    if (pos.Token == tick.Token)
                    {
                        var legMtm = pos.BuySell == BuySell.BUY
                            ? (tick.LTP - pos.AvgEntryPrice) * pos.Qty
                            : (pos.AvgEntryPrice - tick.LTP) * pos.Qty;
                        totalMTM += legMtm;
                    }
                }

                var legsToBreakeven = _breakevenManager.EvaluateBreakeven(_config, totalMTM, _openLegPositions);
                if (legsToBreakeven.Count > 0)
                {
                    _logger.LogInformation("Moving SL to cost for {Count} legs on strategy {Name}", 
                        legsToBreakeven.Count, _config.Name);
                    _eventBus.Publish(new StrategyEvent(_config.Id.ToString(), 
                        StrategyEventType.Other, DateTime.UtcNow, 
                        $"SL moved to cost for {legsToBreakeven.Count} legs"));
                    // In production, this would call _orderManager.ModifyAsync to update SL orders
                }
            }

            // ── WAITING_ENTRY state ──
            if (_config.State == StrategyState.WAITING_ENTRY)
            {
                // G.3: Wait & Trade — capture start price and check condition
                if (_config.WaitAndTradeEnabled && _waitAndTrade != null)
                {
                    if (_underlyingLTPAtStartTime == 0)
                        _underlyingLTPAtStartTime = tick.LTP;

                    if (!_waitConditionSatisfied)
                    {
                        _waitConditionSatisfied = _waitAndTrade.IsEntryConditionSatisfied(
                            _config, tick.LTP, _underlyingLTPAtStartTime);

                        if (!_waitConditionSatisfied) return; // Keep waiting
                        
                        _logger.LogInformation("Wait & Trade condition met for {Name} at LTP {LTP}", 
                            _config.Name, tick.LTP);
                        _eventBus.Publish(new StrategyEvent(_config.Id.ToString(), 
                            StrategyEventType.Other, DateTime.UtcNow, 
                            $"Wait & Trade satisfied: move from {_underlyingLTPAtStartTime} to {tick.LTP}"));
                    }
                }

                // Standard entry evaluation (time, LTP, indicator-based)
                if (await _entryEvaluator.ShouldEnterAsync(_config, tick, DateTime.Now))
                {
                    // G.2: Premium Matching validation before entry
                    if (_config.PremiumMatchingEnabled && _premiumValidator != null)
                    {
                        var legLTPs = new Dictionary<string, decimal>();
                        foreach (var leg in _config.Legs)
                        {
                            legLTPs[leg.Id.ToString()] = tick.LTP; // Simplified — in production, fetch per-leg LTP
                        }

                        bool premiumOk = await _premiumValidator.ValidatePremiumMatchAsync(_config, legLTPs);
                        if (!premiumOk)
                        {
                            _logger.LogDebug("Premium matching check failed for {Name}, skipping entry", _config.Name);
                            return;
                        }
                    }

                    // G.9: Execution Rule filter
                    if (!ExecutionRuleFilter.CanEnter(_config.ExecutionRule))
                    {
                        _logger.LogInformation("Execution rule {Rule} prevents entry for {Name}",
                            _config.ExecutionRule, _config.Name);
                        return;
                    }

                    // ── Execute Entry ──
                    _config.State = StrategyState.ENTERED;
                    _eventBus.Publish(new StrategyEvent(_config.Id.ToString(), 
                        StrategyEventType.EntryPlaced, DateTime.UtcNow, $"Entry triggered at {tick.LTP}"));

                    // Apply execution rule filter to legs
                    var filteredLegs = ExecutionRuleFilter.ApplyRule(_config.Legs, _config.ExecutionRule);
                    var entryConfig = _config with { Legs = filteredLegs };

                    var account = new AccountCredential { BrokerType = _config.BrokerType, ClientID = _config.ClientId };
                    _ = _executionEngine.ExecuteStrategyAsync(entryConfig, account);

                    // Track open positions for breakeven/MTM
                    _openLegPositions.Clear();
                    foreach (var leg in filteredLegs)
                    {
                        _openLegPositions.Add(new LegPosition
                        {
                            StrategyId = _config.Id,
                            LegId = leg.Id,
                            Symbol = leg.Symbol,
                            Token = leg.Token,
                            Exchange = leg.Exchange,
                            BuySell = leg.BuySell,
                            AvgEntryPrice = tick.LTP, // Will be updated by fill
                            Qty = leg.Qty * leg.LotMultiplier,
                            ProductType = leg.ProductType
                        });
                    }

                    _eventBus.Publish(new StrategyEvent(_config.Id.ToString(), 
                        StrategyEventType.EntryFilled, DateTime.UtcNow, "Execution Engine engaged"));
                }
            }
            // ── ENTERED state ──
            else if (_config.State == StrategyState.ENTERED)
            {
                var exitSignal = await _exitEvaluator.ShouldExitAsync(_config, tick, DateTime.Now);
                if (exitSignal != null)
                {
                    _config.State = StrategyState.EXITED;
                    _eventBus.Publish(new StrategyEvent(_config.Id.ToString(), 
                        StrategyEventType.ExitPlaced, DateTime.UtcNow, $"Exit: {exitSignal}"));
                    _logger.LogInformation("Strategy {Name} exited: {Reason}", _config.Name, exitSignal);

                    // Combined Re-entry check
                    if (_reEntryManager.ShouldCombinedReEnter(_config))
                    {
                        var reEntryConfig = _reEntryManager.ProcessCombinedReEntry(_config);
                        _logger.LogInformation("Strategy {Name} triggered combined re-entry", _config.Name);
                        
                        typeof(StrategyConfig).GetProperty("Legs")?.SetValue(_config, reEntryConfig.Legs);
                        _config.State = StrategyState.WAITING_ENTRY;
                        _underlyingLTPAtStartTime = 0; // Reset Wait & Trade
                        _waitConditionSatisfied = false;
                        
                        _eventBus.Publish(new StrategyEvent(_config.Id.ToString(), 
                            StrategyEventType.ReEntry, DateTime.UtcNow, "Combined Re-entry armed"));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _config.State = StrategyState.ERROR;
            _logger.LogError(ex, "Strategy {Name} error", _config.Name);
            _eventBus.Publish(new StrategyEvent(_config.Id.ToString(), 
                StrategyEventType.Error, DateTime.UtcNow, ex.Message));
        }
    }

    private void OnLegExit(OrderBook update)
    {
        // Identify which leg exited
        // Tag format: SL_{strategyId}_{legId}
        var parts = update.Tag.Split('_');
        if (parts.Length < 3) return;
        var legIdStr = parts[2];
        var leg = _config.Legs.FirstOrDefault(l => l.Id.ToString() == legIdStr);
        if (leg == null) return;

        _logger.LogInformation("Leg {LegName} exited via {Tag}", leg.LegName, update.Tag);
        
        // Remove from open positions
        _openLegPositions.RemoveAll(p => p.LegId == leg.Id);

        // Construct LegPosition for re-entry logic
        var exitPos = new LegPosition
        {
            StrategyId = _config.Id,
            LegId = leg.Id,
            Symbol = leg.Symbol,
            Token = leg.Token,
            Exchange = leg.Exchange,
            BuySell = leg.BuySell,
            AvgEntryPrice = update.AvgPrice,
            Qty = update.FilledQty
        };

        CheckLegReEntry(leg, exitPos);
    }

    private Dictionary<Guid, (LegConfig Leg, LegPosition ExitPos)> _pendingLegReEntries = new();

    private void CheckLegReEntry(LegConfig leg, LegPosition exitPos)
    {
        // If immediate re-entry, process it now
        if (leg.LegReEntryType == LegReEntryType.ReEnterImmediately || 
            leg.LegReEntryType == LegReEntryType.ReverseAndReEnterImmediately)
        {
            if (_reEntryManager.ShouldLegReEnter(leg, _config, exitPos, null, DateTime.Now))
            {
                ExecuteLegReEntry(leg);
            }
        }
        else
        {
            // Pending "At Cost" re-entry — will be checked on subsequent ticks
            _pendingLegReEntries[leg.Id] = (leg, exitPos);
            _logger.LogInformation("Leg {LegName} marked for 'At Cost' re-entry check", leg.LegName);
        }
    }

    private async void ExecuteLegReEntry(LegConfig leg)
    {
        var newLeg = _reEntryManager.ProcessLegReEntry(leg, _config);

        var account = new AccountCredential { BrokerType = _config.BrokerType, ClientID = _config.ClientId };
        var tempConfig = _config with { Legs = new List<LegConfig> { newLeg } };
        await _executionEngine.ExecuteStrategyAsync(tempConfig, account);
        
        _eventBus.Publish(new StrategyEvent(_config.Id.ToString(), StrategyEventType.ReEntry, 
            DateTime.UtcNow, $"Leg Re-entry executed: {leg.LegName}"));
    }

    public void Stop()
    {
        _cts.Cancel();
        _subscriptions.Dispose();
        _config.State = StrategyState.IDLE;
        _breakevenManager?.ResetForStrategy(_config.Id);
        _logger.LogInformation("Strategy {Name} stopped", _config.Name);
    }

    public async Task ForceExitAsync(ExitReason reason)
    {
        if (_config.State == StrategyState.ENTERED)
        {
            _config.State = StrategyState.EXITED;
            _logger.LogInformation("Strategy {Name} forced exit: {Reason}", _config.Name, reason);
            _eventBus.Publish(new StrategyEvent(_config.Id.ToString(), 
                StrategyEventType.ExitPlaced, DateTime.UtcNow, $"Forced Exit: {reason}"));
            
            var account = new AccountCredential { BrokerType = _config.BrokerType, ClientID = _config.ClientId };
            var openPositions = new List<Position>();
            await _executionEngine.LiquidateStrategyAsync(_config, account, openPositions);
            _openLegPositions.Clear();
        }
    }

    public void Dispose() => Stop();
}

/// <summary>
/// Evaluates entry conditions for a strategy.
/// Supports: Time-based, LTP condition, Indicator-based (G.10), and Immediate entries.
/// </summary>
public class EntryEvaluator
{
    private readonly ILogger<EntryEvaluator> _logger;
    private readonly IndicatorConditionEvaluator? _indicatorEvaluator;

    public EntryEvaluator(ILogger<EntryEvaluator> logger, IndicatorConditionEvaluator? indicatorEvaluator = null)
    {
        _logger = logger;
        _indicatorEvaluator = indicatorEvaluator;
    }

    public async Task<bool> ShouldEnterAsync(StrategyConfig config, Tick tick, DateTime now)
    {
        var entry = config.Entry;

        // Market hours check (NSE: 9:15 - 15:30 IST)
        var ist = TimeZoneInfo.ConvertTimeFromUtc(now.ToUniversalTime(),
            TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"));
        var marketOpen = new TimeSpan(9, 15, 0);
        var marketClose = new TimeSpan(15, 30, 0);

        if (ist.TimeOfDay < marketOpen || ist.TimeOfDay > marketClose)
            return false;

        // G.6: Positional next-day condition re-check
        if (config.IsPositional && config.CheckConditionNextDayAfter.HasValue)
        {
            if (ist.TimeOfDay < config.CheckConditionNextDayAfter.Value)
                return false;
        }

        // Basic entry type check
        bool basicConditionMet = entry.EntryType switch
        {
            EntryType.Immediate => true,
            EntryType.TimeBased =>
                entry.EntryTime.HasValue && ist.TimeOfDay >= entry.EntryTime.Value &&
                (!entry.EntryWindowEnd.HasValue || ist.TimeOfDay <= entry.EntryWindowEnd.Value),
            EntryType.LTPCondition =>
                entry.LTPOperator == LTPOperator.GreaterEqual
                    ? tick.LTP >= entry.LTPThreshold
                    : tick.LTP <= entry.LTPThreshold,
            _ => false
        };

        if (!basicConditionMet) return false;

        // G.10: Indicator-based entry conditions
        if (config.EntryConditions != null && config.EntryConditions.Conditions.Count > 0 && _indicatorEvaluator != null)
        {
            try
            {
                // Account credential for indicator data fetching — uses the strategy's own clientId
                var account = new AccountCredential { BrokerType = config.BrokerType, ClientID = config.ClientId };

                // Determine the symbol/token to evaluate indicators on
                var firstLeg = config.Legs.FirstOrDefault();
                string evalSymbol = firstLeg?.UnderlyingSymbol ?? firstLeg?.Symbol ?? tick.Symbol;
                string evalToken  = firstLeg?.Token ?? tick.Token;
                Exchange evalExchange = firstLeg?.Exchange ?? Exchange.NSE;

                bool indicatorResult = await _indicatorEvaluator.EvaluateSetAsync(
                    config.EntryConditions, evalSymbol, evalToken, evalExchange, account);

                if (!indicatorResult)
                {
                    _logger.LogDebug("Indicator entry conditions not met for {Name}", config.Name);
                    return false;
                }

                _logger.LogInformation("Indicator entry conditions met for strategy {Name}", config.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Indicator evaluation failed for {Name}, skipping indicator check", config.Name);
                // Optionally continue without indicator check, or return false
            }
        }

        return true;
    }
}

/// <summary>
/// Evaluates exit conditions.
/// Supports: Time-based exits, Indicator-based exits (G.10).
/// </summary>
public class ExitEvaluator
{
    private readonly ILogger<ExitEvaluator> _logger;
    private readonly IndicatorConditionEvaluator? _indicatorEvaluator;

    public ExitEvaluator(ILogger<ExitEvaluator> logger, IndicatorConditionEvaluator? indicatorEvaluator = null)
    {
        _logger = logger;
        _indicatorEvaluator = indicatorEvaluator;
    }

    /// <summary>
    /// Check if the strategy should exit. Returns the exit reason or null.
    /// Now async to support indicator evaluation for exit conditions.
    /// </summary>
    public async Task<ExitReason?> ShouldExitAsync(StrategyConfig config, Tick tick, DateTime now)
    {
        var exit = config.Exit;
        var ist = TimeZoneInfo.ConvertTimeFromUtc(now.ToUniversalTime(),
            TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"));

        // Time-based exit
        if (exit.TimeBasedExit && exit.ExitTime.HasValue && ist.TimeOfDay >= exit.ExitTime.Value)
            return ExitReason.TimeBasedExit;

        // G.6: Auto square-off time
        if (config.DailyAutoSquareOffEnabled && ist.TimeOfDay >= config.DailySquareOffTime)
            return ExitReason.TimeBasedExit;

        // G.6: Expiry day square-off
        if (config.ExpiryDaySquareOffEnabled)
        {
            var nearestExpiry = config.Legs.Any() ? config.Legs.Min(l => l.Expiry) : DateTime.MaxValue;
            if (ist.Date == nearestExpiry.Date && ist.TimeOfDay >= config.ExpiryDaySquareOffTime)
                return ExitReason.TimeBasedExit;
        }

        // G.10: Indicator-based exit conditions
        if (config.ExitConditions != null && config.ExitConditions.Conditions.Count > 0 && _indicatorEvaluator != null)
        {
            try
            {
                var account = new AccountCredential { BrokerType = config.BrokerType, ClientID = config.ClientId };
                var firstLeg = config.Legs.FirstOrDefault();
                string evalSymbol    = firstLeg?.UnderlyingSymbol ?? firstLeg?.Symbol ?? tick.Symbol;
                string evalToken     = firstLeg?.Token ?? tick.Token;
                Exchange evalExchange = firstLeg?.Exchange ?? Exchange.NSE;

                bool indicatorExit = await _indicatorEvaluator.EvaluateSetAsync(
                    config.ExitConditions, evalSymbol, evalToken, evalExchange, account);

                if (indicatorExit)
                {
                    _logger.LogInformation("Indicator exit condition triggered for {Name}", config.Name);
                    return ExitReason.Manual; // Using Manual as closest match for indicator-triggered exit
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Indicator exit evaluation failed for {Name}", config.Name);
            }
        }

        return null; // More complex P&L-based exits checked by RiskManager
    }

    /// <summary>Synchronous fallback for non-indicator exit checks.</summary>
    public ExitReason? ShouldExit(StrategyConfig config, Tick tick, DateTime now)
    {
        var exit = config.Exit;
        var ist = TimeZoneInfo.ConvertTimeFromUtc(now.ToUniversalTime(),
            TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"));

        if (exit.TimeBasedExit && exit.ExitTime.HasValue && ist.TimeOfDay >= exit.ExitTime.Value)
            return ExitReason.TimeBasedExit;

        if (config.DailyAutoSquareOffEnabled && ist.TimeOfDay >= config.DailySquareOffTime)
            return ExitReason.TimeBasedExit;

        return null;
    }
}

/// <summary>Reactive event bus for strategy lifecycle events.</summary>
public class StrategyEventBus
{
    private readonly Subject<StrategyEvent> _subject = new();
    public IObservable<StrategyEvent> Events => _subject.AsObservable();

    public void Publish(StrategyEvent evt)
    {
        _subject.OnNext(evt);
    }
}

/// <summary>Tracks real-time MTM per strategy from tick stream.</summary>
public class PositionTracker
{
    private readonly Subject<PositionSummary> _mtmSubject = new();
    private readonly ConcurrentDictionary<string, PositionSummary> _summaries = new();
    private readonly ConcurrentDictionary<string, Dictionary<string, decimal>> _strategyTokenMtm = new();
    private readonly ILogger<PositionTracker> _logger;

    public IObservable<PositionSummary> MTMUpdates => _mtmSubject.AsObservable();

    public PositionTracker(ILogger<PositionTracker> logger)
    {
        _logger = logger;
    }

    public void UpdateMTM(string strategyId, Tick tick, List<LegPosition> positions)
    {
        var summary = _summaries.GetOrAdd(strategyId, id => new PositionSummary { StrategyId = id });
        var tokenMtmMap = _strategyTokenMtm.GetOrAdd(strategyId, _ => new Dictionary<string, decimal>());

        foreach (var pos in positions)
        {
            // Buy: (LTP - Avg) * Qty
            // Sell: (Avg - LTP) * Qty
            decimal mtm = pos.BuySell == BuySell.BUY 
                ? (tick.LTP - pos.AvgEntryPrice) * pos.Qty 
                : (pos.AvgEntryPrice - tick.LTP) * pos.Qty;
            
            tokenMtmMap[pos.Token] = mtm;
        }

        summary.UnrealizedPnL = tokenMtmMap.Values.Sum();
        summary.TotalMTM = summary.RealizedPnL + summary.UnrealizedPnL;
        _mtmSubject.OnNext(summary);
    }

    public PositionSummary? GetSummary(string strategyId) => _summaries.GetValueOrDefault(strategyId);

    public decimal TotalMTM => _summaries.Values.Sum(s => s.TotalMTM);
    public decimal RealizedPnL => _summaries.Values.Sum(s => s.RealizedPnL);
    public List<PositionSummary> Positions => _summaries.Values.ToList();
}

/// <summary>SL management — places and tracks stop-loss orders on broker.</summary>
public class SLManager
{
    private readonly IOrderManager _orderManager;
    private readonly ILogger<SLManager> _logger;
    private readonly Dictionary<string, string> _slOrderIds = new(); // legId -> slOrderId

    public SLManager(IOrderManager orderManager, ILogger<SLManager> logger)
    {
        _orderManager = orderManager;
        _logger = logger;
    }

    public async Task PlaceSLOrdersAsync(StrategyConfig config, List<OrderBook> entryFills, AccountCredential account)
    {
        foreach (var leg in config.Legs)
        {
            if (leg.SLPoints <= 0 && leg.SLPercent <= 0) continue;

            var fill = entryFills.FirstOrDefault(f => f.Token == leg.Token);
            if (fill == null) continue;

            decimal slPrice = leg.SLPoints > 0
                ? (leg.BuySell == BuySell.BUY ? fill.AvgPrice - leg.SLPoints : fill.AvgPrice + leg.SLPoints)
                : (leg.BuySell == BuySell.BUY ? fill.AvgPrice * (1 - leg.SLPercent / 100) : fill.AvgPrice * (1 + leg.SLPercent / 100));

            var slRequest = new OrderRequest
            {
                Symbol = leg.Symbol,
                Token = leg.Token,
                Exchange = leg.Exchange,
                BuySell = leg.BuySell == BuySell.BUY ? BuySell.SELL : BuySell.BUY,
                Qty = leg.Qty * leg.LotMultiplier,
                OrderType = OrderType.SL_M,
                ProductType = leg.ProductType,
                TriggerPrice = slPrice,
                Tag = $"SL_{config.Id}_{leg.Id}"
            };

            var result = await _orderManager.PlaceAsync(slRequest, account);
            _slOrderIds[leg.Id.ToString()] = result.OrderID;
            _logger.LogInformation("SL order placed for {Leg}: {OrderId} at {Price}", leg.LegName, result.OrderID, slPrice);
        }
    }

    public async Task CancelSLOrdersAsync(AccountCredential account)
    {
        foreach (var orderId in _slOrderIds.Values)
        {
            await _orderManager.CancelAsync(orderId, account);
        }
        _slOrderIds.Clear();
        _logger.LogInformation("All SL orders cancelled");
    }
}
