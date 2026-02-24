using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cognexalgo.Core.Application.Interfaces;
using Cognexalgo.Core.Domain.Entities;
using Cognexalgo.Core.Domain.Enums;
using Cognexalgo.Core.Domain.ValueObjects;

namespace Cognexalgo.Core.Application.Services
{
    /// <summary>
    /// Strategy-Level RMS (Module 6):
    /// ┌─────────────────────────────────────────────────────┐
    /// │ Max Loss (₹)          → auto-exit all legs if hit   │
    /// │ Max Profit (₹)        → auto-exit all legs if hit   │
    /// │ Trailing SL (₹)       → SL moves up as profit grows │
    /// │ Lock Profit At (₹)    → when profit hits X, lock Y  │
    /// │ Max Orders Per Day    → stop after N orders          │
    /// │ Max Re-entries        → max N re-entries after SL    │
    /// │ Time-based Exit       → force exit at HH:MM          │
    /// │ Expiry Day Rules      → special rules on expiry day  │
    /// └─────────────────────────────────────────────────────┘
    /// </summary>
    public class StrategyRmsService
    {
        public event Action<string, RmsRuleType, decimal>? OnRmsBreach;
        public event Action<string, string>? OnLog;

        // Per-strategy tracking
        private readonly ConcurrentDictionary<string, RmsTracker> _trackers = new();

        public void RegisterStrategy(string strategyId, RmsConfig config)
        {
            _trackers[strategyId] = new RmsTracker(config);
        }

        /// <summary>
        /// Pre-check: Should the OMS be allowed to place this order?
        /// Returns (canExecute, blockReason).
        /// </summary>
        public (bool CanExecute, string? BlockReason) CanExecute(string strategyId, int orderCountToday)
        {
            if (!_trackers.TryGetValue(strategyId, out var tracker))
                return (true, null);

            var cfg = tracker.Config;

            // Max orders per day
            if (cfg.MaxOrdersPerDay > 0 && orderCountToday >= cfg.MaxOrdersPerDay)
                return (false, $"Max orders/day reached ({cfg.MaxOrdersPerDay})");

            return (true, null);
        }

        /// <summary>
        /// Check all RMS rules against current P&L.
        /// Returns a list of breached rules (empty = all clear).
        /// </summary>
        public List<RmsBreachResult> CheckRules(string strategyId, decimal currentPnl, DateTime now)
        {
            var breaches = new List<RmsBreachResult>();
            if (!_trackers.TryGetValue(strategyId, out var tracker))
                return breaches;

            var cfg = tracker.Config;

            // ─── Max Loss ────────────────────────────────────────
            if (cfg.MaxLoss > 0 && currentPnl <= -cfg.MaxLoss)
            {
                breaches.Add(new RmsBreachResult(RmsRuleType.MaxLoss, currentPnl, -cfg.MaxLoss));
            }

            // ─── Max Profit ──────────────────────────────────────
            if (cfg.MaxProfit > 0 && currentPnl >= cfg.MaxProfit)
            {
                breaches.Add(new RmsBreachResult(RmsRuleType.MaxProfit, currentPnl, cfg.MaxProfit));
            }

            // ─── Trailing SL (high watermark) ────────────────────
            if (cfg.TrailingSL > 0)
            {
                // Update high watermark
                if (currentPnl > tracker.HighWatermark)
                    tracker.HighWatermark = currentPnl;

                decimal trailAmount = cfg.TrailingIsPercent
                    ? tracker.HighWatermark * cfg.TrailingSL / 100
                    : cfg.TrailingSL;

                decimal trailExitAt = tracker.HighWatermark - trailAmount;

                if (tracker.HighWatermark > 0 && currentPnl <= trailExitAt)
                {
                    breaches.Add(new RmsBreachResult(
                        RmsRuleType.TrailingSL, currentPnl, trailExitAt,
                        $"Peak={tracker.HighWatermark:F0}, Trail={trailAmount:F0}"));
                }
            }

            // ─── Lock Profit ─────────────────────────────────────
            if (cfg.LockProfitAt > 0 && currentPnl >= cfg.LockProfitAt)
            {
                tracker.IsProfitLocked = true;
                tracker.LockedMinimum = cfg.LockProfitTo;
            }

            if (tracker.IsProfitLocked && currentPnl <= tracker.LockedMinimum)
            {
                breaches.Add(new RmsBreachResult(
                    RmsRuleType.LockProfit, currentPnl, tracker.LockedMinimum,
                    $"Locked at {cfg.LockProfitAt:F0} → min {cfg.LockProfitTo:F0}"));
            }

            // ─── Time-based Exit ─────────────────────────────────
            if (!string.IsNullOrEmpty(cfg.TimeBasedExitTime))
            {
                if (TimeSpan.TryParse(cfg.TimeBasedExitTime, out var exitTime))
                {
                    if (now.TimeOfDay >= exitTime)
                    {
                        breaches.Add(new RmsBreachResult(
                            RmsRuleType.TimeBasedExit, currentPnl, 0,
                            $"Time exit at {cfg.TimeBasedExitTime}"));
                    }
                }
            }

            // Fire events
            foreach (var breach in breaches)
            {
                OnRmsBreach?.Invoke(strategyId, breach.RuleType, breach.CurrentValue);
                Log("WARN", $"[RMS] {strategyId}: {breach.RuleType} breached " +
                    $"(current={breach.CurrentValue:F0}, threshold={breach.ThresholdValue:F0}) {breach.Detail}");
            }

            return breaches;
        }

        public void ResetTracker(string strategyId)
        {
            _trackers.TryRemove(strategyId, out _);
        }

        private void Log(string level, string msg) => OnLog?.Invoke(level, msg);
    }

    /// <summary>
    /// Account-Level RMS (Module 6 — global across all strategies):
    /// ┌─────────────────────────────────────────────────────┐
    /// │ Daily Max Loss (₹)    → halt ALL strategies if hit  │
    /// │ Daily Max Profit (₹)  → halt ALL strategies if hit  │
    /// │ Max Concurrent Strategies → cap on simultaneous     │
    /// │ Max Margin Utilization (%) → don't exceed X% margin │
    /// │ Kill Switch           → one-click exit everything   │
    /// └─────────────────────────────────────────────────────┘
    /// </summary>
    public class AccountRmsService
    {
        public decimal DailyMaxLoss { get; set; } = 0;
        public decimal DailyMaxProfit { get; set; } = 0;
        public int MaxConcurrentStrategies { get; set; } = 10;
        public decimal MaxMarginUtilizationPercent { get; set; } = 80;
        public bool IsKillSwitchActive { get; set; } = false;

        public decimal TotalDailyPnl { get; set; } = 0;

        public event Action<RmsRuleType, string>? OnAccountBreach;

        /// <summary>
        /// Check account-level RMS before any strategy action.
        /// </summary>
        public (bool IsBlocked, string? Reason) CheckAccountLimits(
            int activeStrategyCount, decimal marginUtilization)
        {
            if (IsKillSwitchActive)
                return (true, "Kill Switch is ACTIVE — all trading halted");

            if (DailyMaxLoss > 0 && TotalDailyPnl <= -DailyMaxLoss)
            {
                OnAccountBreach?.Invoke(RmsRuleType.DailyMaxLoss, $"Daily loss limit hit: ₹{TotalDailyPnl:F0}");
                return (true, $"Daily max loss of ₹{DailyMaxLoss} reached");
            }

            if (DailyMaxProfit > 0 && TotalDailyPnl >= DailyMaxProfit)
            {
                OnAccountBreach?.Invoke(RmsRuleType.DailyMaxProfit, $"Daily profit target hit: ₹{TotalDailyPnl:F0}");
                return (true, $"Daily max profit of ₹{DailyMaxProfit} reached");
            }

            if (MaxConcurrentStrategies > 0 && activeStrategyCount >= MaxConcurrentStrategies)
                return (true, $"Max concurrent strategies ({MaxConcurrentStrategies}) reached");

            if (MaxMarginUtilizationPercent > 0 && marginUtilization >= MaxMarginUtilizationPercent)
                return (true, $"Margin utilization {marginUtilization:F1}% exceeds {MaxMarginUtilizationPercent}%");

            return (false, null);
        }

        /// <summary>Activate/Deactivate Kill Switch.</summary>
        public void ToggleKillSwitch(bool active)
        {
            IsKillSwitchActive = active;
            OnAccountBreach?.Invoke(RmsRuleType.KillSwitch,
                active ? "Kill Switch ACTIVATED" : "Kill Switch DEACTIVATED");
        }
    }

    // ─── Supporting Types ────────────────────────────────────────

    internal class RmsTracker
    {
        public RmsConfig Config { get; }
        public decimal HighWatermark { get; set; } = 0;
        public bool IsProfitLocked { get; set; } = false;
        public decimal LockedMinimum { get; set; } = 0;

        public RmsTracker(RmsConfig config) { Config = config; }
    }

    public class RmsBreachResult
    {
        public RmsRuleType RuleType { get; set; }
        public decimal CurrentValue { get; set; }
        public decimal ThresholdValue { get; set; }
        public string? Detail { get; set; }

        public RmsBreachResult(RmsRuleType ruleType, decimal current, decimal threshold, string? detail = null)
        {
            RuleType = ruleType;
            CurrentValue = current;
            ThresholdValue = threshold;
            Detail = detail;
        }
    }
}
