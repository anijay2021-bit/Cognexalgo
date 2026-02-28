using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cognexalgo.Core.Application.Interfaces;
using Cognexalgo.Core.Domain.Entities;
using Cognexalgo.Core.Domain.Enums;
using Cognexalgo.Core.Domain.Strategies;
using Cognexalgo.Core.Domain.ValueObjects;
using Cognexalgo.Core.Infrastructure.Services;
using Cognexalgo.Core.Models;
using Newtonsoft.Json;

namespace Cognexalgo.Core.Application.Services
{
    /// <summary>
    /// Strategy Orchestrator (Module 9 — Strategy Isolation):
    /// - Manages Dictionary<string, StrategyExecutionContext>
    /// - Each strategy runs in its own Task with isolated error handling
    /// - Circuit breaker: 3 consecutive errors → auto-pause
    /// - Crash recovery: reload state from DB
    /// </summary>
    public class StrategyOrchestrator
    {
        private readonly ConcurrentDictionary<string, StrategyExecutionContext> _contexts = new();
        private readonly IStrategyRepository _strategyRepo;
        private readonly SignalEngine _signalEngine;
        private readonly StrategyRmsService _rmsService;

        public event Action<string, string>? OnLog;
        public event Action<string, StrategyStatus>? OnStatusChanged;

        public int ActiveCount => _contexts.Count(c => c.Value.IsRunning);
        public IReadOnlyDictionary<string, StrategyExecutionContext> Contexts => _contexts;

        public StrategyOrchestrator(
            IStrategyRepository strategyRepo,
            SignalEngine signalEngine,
            StrategyRmsService rmsService)
        {
            _strategyRepo = strategyRepo;
            _signalEngine = signalEngine;
            _rmsService = rmsService;
        }

        /// <summary>Start a strategy in its own isolated Task.</summary>
        public async Task StartStrategyAsync(StrategyV2Base strategy)
        {
            if (_contexts.ContainsKey(strategy.StrategyId))
            {
                Log("WARN", $"Strategy {strategy.StrategyId} already running");
                return;
            }

            var context = new StrategyExecutionContext(strategy);
            _contexts[strategy.StrategyId] = context;

            // Wire up signal handler
            strategy.OnSignalFired += async (signal) =>
            {
                await _signalEngine.ProcessSignalAsync(signal, strategy);
            };

            strategy.OnLog += (level, msg) => Log(level, msg);
            strategy.OnError += (ex) =>
                Log("ERROR", $"[{strategy.Name}] {ex.Message}");

            // Initialize
            await strategy.InitializeAsync(context.CancellationToken);
            strategy.Status = StrategyStatus.Active;
            context.IsRunning = true;

            Log("INFO", $"▶ Strategy started: {strategy.Name} ({strategy.StrategyId})");
            OnStatusChanged?.Invoke(strategy.StrategyId, StrategyStatus.Active);
        }

        /// <summary>Feed a tick to all running strategies (isolated execution).</summary>
        public async Task DispatchTickAsync(TickContext tick)
        {
            var tasks = _contexts.Values
                .Where(c => c.IsRunning && !c.Strategy.IsCircuitBroken)
                .Select(c => SafeExecuteAsync(c, tick));

            await Task.WhenAll(tasks);
        }

        /// <summary>Execute a single strategy tick with full error isolation + RMS enforcement.</summary>
        private async Task SafeExecuteAsync(StrategyExecutionContext ctx, TickContext tick)
        {
            try
            {
                await ctx.Strategy.OnTickAsync(tick, ctx.CancellationToken);

                // ── Update DailyPnl from strategy legs ──────────────────
                if (ctx.Strategy is HybridV2Strategy hybrid)
                {
                    ctx.DailyPnl = (decimal)hybrid.GetConfig().Legs
                        .Where(l => l.Status == "OPEN")
                        .Sum(l => (l.Action == Models.ActionType.Sell
                            ? l.EntryPrice - l.Ltp
                            : l.Ltp - l.EntryPrice) * l.TotalLots);

                    if (ctx.DailyPnl > ctx.HighWatermark)
                        ctx.HighWatermark = ctx.DailyPnl;
                }

                // ── RMS enforcement ─────────────────────────────────────
                var breaches = _rmsService.CheckRules(ctx.Strategy.StrategyId, ctx.DailyPnl, DateTime.Now);
                if (breaches.Count > 0)
                {
                    var signal = new Domain.Entities.Signal
                    {
                        StrategyId = ctx.Strategy.StrategyId,
                        SignalType = SignalType.ForceExit,
                        TriggerCondition = $"RMS: {string.Join(", ", breaches.Select(b => b.RuleType))}"
                    };
                    await _signalEngine.ProcessSignalAsync(signal, ctx.Strategy);

                    // Force-exit all open legs
                    if (ctx.Strategy is HybridV2Strategy hybridRms)
                    {
                        foreach (var leg in hybridRms.GetConfig().Legs.Where(l => l.Status == "OPEN"))
                        {
                            leg.Status = "EXITED";
                            leg.ExitPrice = leg.Ltp;
                            leg.ExitTime = DateTime.Now;
                            leg.ExitReason = "RMS";
                        }
                    }

                    ctx.IsRunning = false;
                    ctx.Strategy.CurrentState = SignalState.COMPLETED;
                    OnStatusChanged?.Invoke(ctx.Strategy.StrategyId, StrategyStatus.Error);
                    Log("WARN", $"[RMS] ForceExit: {ctx.Strategy.Name} — " +
                        $"{string.Join(", ", breaches.Select(b => $"{b.RuleType}={b.CurrentValue:F0}"))}");
                }

                // ── Periodic state snapshot (every 30s) ────────────
                if ((DateTime.UtcNow - ctx.LastSnapshotTime).TotalSeconds >= 30)
                {
                    ctx.LastSnapshotTime = DateTime.UtcNow;
                    try
                    {
                        var snapshot = ctx.Strategy.CaptureSnapshot();
                        var json = JsonConvert.SerializeObject(snapshot);
                        var dbStrategy = await _strategyRepo.GetByIdAsync(ctx.Strategy.StrategyId);
                        if (dbStrategy != null)
                        {
                            dbStrategy.StateMachineSnapshot = json;
                            dbStrategy.Status = StrategyStatus.Active;
                            await _strategyRepo.UpdateAsync(dbStrategy);
                        }
                    }
                    catch { /* Snapshot failure is non-critical */ }
                }
            }
            catch (OperationCanceledException)
            {
                // Strategy was cancelled, normal flow
            }
            catch (Exception ex)
            {
                ctx.Strategy.RecordError(ex);

                if (ctx.Strategy.IsCircuitBroken)
                {
                    ctx.IsRunning = false;
                    OnStatusChanged?.Invoke(ctx.Strategy.StrategyId, StrategyStatus.Error);
                    Log("ERROR", $"⚠ Strategy {ctx.Strategy.Name} circuit breaker tripped!");
                }
            }
        }

        /// <summary>Stop a specific strategy and persist clean stop to DB.</summary>
        public async Task StopStrategyAsync(string strategyId)
        {
            if (_contexts.TryGetValue(strategyId, out var ctx))
            {
                ctx.Strategy.Stop();
                ctx.IsRunning = false;
                _signalEngine.ResetStrategy(strategyId);
                Log("INFO", $"■ Strategy stopped: {ctx.Strategy.Name}");
                OnStatusChanged?.Invoke(strategyId, StrategyStatus.Paused);

                // Persist clean stop to DB
                try
                {
                    var dbStrategy = await _strategyRepo.GetByIdAsync(strategyId);
                    if (dbStrategy != null)
                    {
                        dbStrategy.Status = StrategyStatus.Paused;
                        dbStrategy.StateMachineSnapshot = null; // Clean stop — no recovery needed
                        await _strategyRepo.UpdateAsync(dbStrategy);
                    }
                }
                catch { /* Non-critical */ }
            }
        }

        /// <summary>Stop a specific strategy (sync wrapper).</summary>
        public void StopStrategy(string strategyId)
        {
            _ = StopStrategyAsync(strategyId);
        }

        /// <summary>Stop ALL strategies (Kill Switch).</summary>
        public void StopAll()
        {
            foreach (var kvp in _contexts)
            {
                kvp.Value.Strategy.Stop();
                kvp.Value.IsRunning = false;
                _signalEngine.ResetStrategy(kvp.Key);
            }
            Log("WARN", "⛔ KILL SWITCH: All strategies stopped");
        }

        /// <summary>Remove a stopped strategy from the orchestrator.</summary>
        public void RemoveStrategy(string strategyId)
        {
            if (_contexts.TryRemove(strategyId, out var ctx))
            {
                if (ctx.IsRunning) ctx.Strategy.Stop();
                ctx.Dispose();
            }
        }

        /// <summary>Recover strategies from DB after crash (Module 9 requirement).</summary>
        public async Task RecoverFromCrashAsync()
        {
            var activeStrategies = await _strategyRepo.GetActiveAsync();
            Log("INFO", $"Crash recovery: found {activeStrategies.Count} active strategies in DB");

            foreach (var s in activeStrategies)
            {
                if (string.IsNullOrEmpty(s.StateMachineSnapshot))
                {
                    Log("WARN", $"  {s.StrategyId}: no snapshot, marking Paused");
                    s.Status = StrategyStatus.Paused;
                    await _strategyRepo.UpdateAsync(s);
                    continue;
                }

                try
                {
                    var snapshot = JsonConvert.DeserializeObject<StrategyStateSnapshot>(s.StateMachineSnapshot);
                    Log("INFO", $"  {s.StrategyId}: state={snapshot?.CurrentState}, legs={snapshot?.Legs.Count}");

                    // Mark as paused — user must manually restart after reviewing state
                    s.Status = StrategyStatus.Paused;
                    await _strategyRepo.UpdateAsync(s);
                    Log("INFO", $"  {s.StrategyId}: marked Paused for manual review");
                }
                catch (Exception ex)
                {
                    Log("ERROR", $"  {s.StrategyId}: snapshot parse error: {ex.Message}");
                    s.Status = StrategyStatus.Error;
                    await _strategyRepo.UpdateAsync(s);
                }
            }
        }

        private void Log(string level, string msg) => OnLog?.Invoke(level, msg);
    }

    /// <summary>
    /// Execution context for a single strategy (Module 9).
    /// Provides isolated cancellation, RMS counters, and order queue.
    /// </summary>
    public class StrategyExecutionContext : IDisposable
    {
        public StrategyV2Base Strategy { get; }
        public CancellationTokenSource Cts { get; private set; }
        public CancellationToken CancellationToken => Cts.Token;
        public bool IsRunning { get; set; }

        // Per-strategy RMS counters
        public int OrderCountToday { get; set; } = 0;
        public int ReEntryCount { get; set; } = 0;
        public decimal DailyPnl { get; set; } = 0;
        public decimal HighWatermark { get; set; } = 0;
        public DateTime LastSnapshotTime { get; set; } = DateTime.MinValue;

        // Thread-safe order queue
        public ConcurrentQueue<Domain.Entities.Order> OrderQueue { get; } = new();

        public StrategyExecutionContext(StrategyV2Base strategy)
        {
            Strategy = strategy;
            Cts = strategy.Cts;
        }

        public void Dispose()
        {
            Cts?.Dispose();
        }
    }
}
