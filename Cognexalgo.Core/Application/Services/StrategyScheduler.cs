using System;
using System.Collections.Generic;
using System.Linq;
using Cognexalgo.Core.Models;

namespace Cognexalgo.Core.Application.Services
{
    /// <summary>
    /// Time-based strategy scheduler.
    /// Call <see cref="GetReadyStrategies"/> on every market tick.
    /// Returns <see cref="HybridStrategyConfig"/> objects for schedules whose EntryTime has arrived
    /// (at most once per calendar day per schedule).
    /// </summary>
    public class StrategyScheduler
    {
        private readonly List<ScheduledStrategyConfig> _schedules = new();

        public IReadOnlyList<ScheduledStrategyConfig> Schedules => _schedules.AsReadOnly();

        public void Add(ScheduledStrategyConfig config)    => _schedules.Add(config);
        public void Remove(string id)                       => _schedules.RemoveAll(s => s.Id == id);
        public void SetActive(string id, bool active)       => _schedules.FirstOrDefault(s => s.Id == id)
                                                                   .Let(s => { if (s != null) s.IsActive = active; });

        /// <summary>
        /// Returns configs that are ready to start right now.
        /// Each schedule fires at most once per calendar day.
        /// </summary>
        public List<HybridStrategyConfig> GetReadyStrategies(DateTime now)
        {
            var ready = new List<HybridStrategyConfig>();
            var today = DateOnly.FromDateTime(now);
            var currentTime = TimeOnly.FromDateTime(now);

            foreach (var schedule in _schedules.Where(s => s.IsActive))
            {
                if (schedule.LastFiredDate == today) continue;          // already fired today
                if (currentTime < schedule.EntryTime) continue;         // entry time not yet

                schedule.LastFiredDate = today;
                ready.Add(BuildConfig(schedule));
            }

            return ready;
        }

        // ── Config builder ────────────────────────────────────────────────────────

        private static HybridStrategyConfig BuildConfig(ScheduledStrategyConfig s) => new()
        {
            Id            = Math.Abs(Guid.NewGuid().GetHashCode() % 90000) + 10000,
            Name          = $"{s.StrategyName} [{s.EntryTime:HH\\:mm}]",
            Legs          = BuildLegs(s),
            CandleStartTime = s.EntryTime.ToString("HH:mm"),
            SquareOffTime   = s.ExitTime.ToString("HH:mm"),
            IsLiveMode    = s.IsLiveMode,
            IsActive      = true,
        };

        private static List<StrategyLeg> BuildLegs(ScheduledStrategyConfig s)
        {
            bool useCP = s.StrikeMode == "ClosestPremium";

            // Factory: ATMPoint leg (fixed offset from ATM)
            StrategyLeg L(OptionType opt, ActionType action, string offset) => new()
            {
                Index            = s.Symbol,
                OptionType       = opt,
                Action           = action,
                TotalLots        = s.TotalLots,
                Mode             = StrikeSelectionMode.ATMPoint,
                ATMOffset        = offset,
                ProductType      = "MIS",
                ExpiryType       = "Weekly",
                StopLossPercent  = s.SlPercent,
                TargetPercent    = s.TargetPercent,
            };

            // Factory: ClosestPremium leg — finds strike with LTP ≈ TargetPremium
            StrategyLeg CP(OptionType opt, ActionType action) => new()
            {
                Index            = s.Symbol,
                OptionType       = opt,
                Action           = action,
                TotalLots        = s.TotalLots,
                Mode             = StrikeSelectionMode.ClosestPremium,
                TargetPremium    = s.TargetPremium,
                PremiumOperator  = s.PremiumOperator,
                PremiumTolerance = s.PremiumTolerance,
                WaitForMatch     = s.PremiumTolerance > 0,
                ProductType      = "MIS",
                ExpiryType       = "Weekly",
                StopLossPercent  = s.SlPercent,
                TargetPercent    = s.TargetPercent,
            };

            return s.StrategyName switch
            {
                // ── Straddle ─────────────────────────────────────────────────────
                "Short Straddle" => useCP
                    ? new() { CP(OptionType.Call, ActionType.Sell), CP(OptionType.Put, ActionType.Sell) }
                    : new() { L(OptionType.Call, ActionType.Sell, "ATM"), L(OptionType.Put, ActionType.Sell, "ATM") },

                "Long Straddle" => useCP
                    ? new() { CP(OptionType.Call, ActionType.Buy), CP(OptionType.Put, ActionType.Buy) }
                    : new() { L(OptionType.Call, ActionType.Buy, "ATM"), L(OptionType.Put, ActionType.Buy, "ATM") },

                // ── Strangle ─────────────────────────────────────────────────────
                // CP strangle: each side independently picks strike closest to TargetPremium
                "Short Strangle" => useCP
                    ? new() { CP(OptionType.Call, ActionType.Sell), CP(OptionType.Put, ActionType.Sell) }
                    : new() { L(OptionType.Call, ActionType.Sell, "ATM+200"), L(OptionType.Put, ActionType.Sell, "ATM-200") },

                "Long Strangle" => useCP
                    ? new() { CP(OptionType.Call, ActionType.Buy), CP(OptionType.Put, ActionType.Buy) }
                    : new() { L(OptionType.Call, ActionType.Buy, "ATM+200"), L(OptionType.Put, ActionType.Buy, "ATM-200") },
                "Short Iron Condor" => new()
                {
                    L(OptionType.Put,  ActionType.Buy,  "ATM-400"),
                    L(OptionType.Put,  ActionType.Sell, "ATM-200"),
                    L(OptionType.Call, ActionType.Sell, "ATM+200"),
                    L(OptionType.Call, ActionType.Buy,  "ATM+400"),
                },
                "Short Iron Butterfly" => new()
                {
                    L(OptionType.Put,  ActionType.Buy,  "ATM-200"),
                    L(OptionType.Put,  ActionType.Sell, "ATM"),
                    L(OptionType.Call, ActionType.Sell, "ATM"),
                    L(OptionType.Call, ActionType.Buy,  "ATM+200"),
                },
                "Jade Lizard" => new()
                {
                    L(OptionType.Put,  ActionType.Sell, "ATM-200"),
                    L(OptionType.Call, ActionType.Sell, "ATM+100"),
                    L(OptionType.Call, ActionType.Buy,  "ATM+300"),
                },
                "Bull Put Spread" => new()
                {
                    L(OptionType.Put, ActionType.Sell, "ATM"),
                    L(OptionType.Put, ActionType.Buy,  "ATM-200"),
                },
                "Bear Call Spread" => new()
                {
                    L(OptionType.Call, ActionType.Sell, "ATM"),
                    L(OptionType.Call, ActionType.Buy,  "ATM+200"),
                },
                "Bull Call Spread" => new()
                {
                    L(OptionType.Call, ActionType.Buy,  "ATM"),
                    L(OptionType.Call, ActionType.Sell, "ATM+200"),
                },
                "Bear Put Spread" => new()
                {
                    L(OptionType.Put, ActionType.Buy,  "ATM"),
                    L(OptionType.Put, ActionType.Sell, "ATM-200"),
                },
                _ => throw new InvalidOperationException($"Unknown strategy template: '{s.StrategyName}'")
            };
        }
    }

    // Minimal helper to avoid null-ref on SetActive
    internal static class NullExt
    {
        internal static T? Let<T>(this T? obj, Action<T?> action) where T : class { action(obj); return obj; }
    }
}
