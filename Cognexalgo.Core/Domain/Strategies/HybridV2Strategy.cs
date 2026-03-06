using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cognexalgo.Core.Domain.Entities;
using Cognexalgo.Core.Domain.Enums;
using Cognexalgo.Core.Domain.Indicators;
using Cognexalgo.Core.Models;
using Cognexalgo.Core.Services;

namespace Cognexalgo.Core.Domain.Strategies
{
    /// <summary>
    /// Full StrategyV2Base wrapper for HybridStrategyConfig.
    /// Provides entry/exit/SL/target monitoring and re-entry logic
    /// for all legs defined in the HybridStrategyConfig.
    /// </summary>
    public class HybridV2Strategy : StrategyV2Base
    {
        private readonly HybridStrategyConfig _config;

        // F1: Per-leg trailing SL — tracks best price seen for each leg (keyed by leg index)
        private readonly Dictionary<int, double> _legTrailBestPrices = new();

        // F2: Indicator engine for entry conditions
        private readonly IndicatorEngine _indicatorEngine = new();
        private HistoryCacheService? _historyCache;

        // Edge-trigger: tracks per-leg whether ALL indicator conditions were met on the previous tick.
        // Entry is only allowed when conditions flip false → true (the crossing candle).
        // Without this, a condition like "Price < EMA" would fire on every tick it remains true.
        private readonly Dictionary<int, bool> _prevEntryConditionsMet = new();

        // F8: Underlying price at strategy first-entry (for UnderlyingMove trigger)
        private double _strategyEntrySpotLtp = 0;

        // Fallback lot sizes per SEBI circular (effective Jan 2026).
        // Used when OptionChainItem.LotSize is 0 (not returned by option chain API).
        private static readonly Dictionary<string, int> _fallbackLotSizes = new(StringComparer.OrdinalIgnoreCase)
        {
            { "NIFTY",      65 },
            { "BANKNIFTY",  30 },
            { "FINNIFTY",   60 },
            { "MIDCPNIFTY", 120 },
            { "SENSEX",     20 },
        };

        private static int ResolvedLotSize(Models.StrategyLeg leg)
        {
            if (leg.LotSize > 0) return leg.LotSize;
            var key = leg.Index?.ToUpperInvariant() ?? "";
            return _fallbackLotSizes.TryGetValue(key, out var sz) ? sz : 1;
        }

        public HybridV2Strategy(HybridStrategyConfig config)
        {
            _config    = config ?? throw new ArgumentNullException(nameof(config));

            StrategyId  = !string.IsNullOrEmpty(config.V2Id)
                          ? config.V2Id
                          : $"HYB-{config.Id}-{DateTime.Now:yyyyMMdd}";

            Name        = config.Name ?? "HybridStrategy";
            Type        = StrategyType.CSTM;
            TradingMode = config.IsLiveMode ? TradingMode.LiveTrade : TradingMode.PaperTrade;
        }

        /// <summary>F2: Inject history cache so indicators can warm up on start.</summary>
        public void SetHistoryCacheService(HistoryCacheService cache) => _historyCache = cache;

        /// <summary>F2: Load index candles into IndicatorEngine for all timeframes used in entry conditions.</summary>
        public override async Task InitializeAsync(CancellationToken ct)
        {
            if (_historyCache == null) return;

            var usedIndices = _config.Legs
                .Where(l => l.EntryConditions?.Count > 0)
                .Select(l => l.Index)
                .Distinct()
                .ToList();

            var usedTimeFrames = _config.Legs
                .SelectMany(l => l.EntryConditions ?? new())
                .Select(c => c.TimeFrame)
                .Distinct()
                .ToList();

            foreach (var index in usedIndices)
            {
                string symbol = index.ToUpper(); // "NIFTY" or "BANKNIFTY"
                foreach (var tfStr in usedTimeFrames)
                {
                    string interval = TimeFrameToInterval(tfStr);
                    var quotes = await _historyCache.GetHistoryAsync(symbol, interval, 60);
                    if (quotes.Count > 0 && Enum.TryParse<TimeFrame>(tfStr, out var tf))
                        _indicatorEngine.LoadHistory(tf, quotes);
                }
            }

            if (_indicatorEngine.IsWarmedUp)
                Log("INFO", $"[{Name}] IndicatorEngine warmed up");
        }

        /// <summary>Expose config for RMS P&L computation in Orchestrator.</summary>
        public HybridStrategyConfig GetConfig() => _config;

        /// <summary>Capture leg-level state for crash recovery.</summary>
        public override StrategyStateSnapshot CaptureSnapshot()
        {
            var snap = base.CaptureSnapshot();
            snap.Legs = _config.Legs.Select(l => new LegSnapshot
            {
                Status = l.Status,
                ExitReason = l.ExitReason,
                CurrentReEntry = l.CurrentReEntry,
                EntryPrice = l.EntryPrice,
                ExitPrice = l.ExitPrice,
                Ltp = l.Ltp,
                CalculatedStrike = l.CalculatedStrike,
                SymbolToken = l.SymbolToken,
                EntryTime = l.EntryTime,
                ExitTime = l.ExitTime
            }).ToList();
            return snap;
        }

        /// <summary>
        /// Called by the Orchestrator on every market tick.
        /// Processes entry/exit/SL/target/re-entry for all configured legs.
        /// </summary>
        public override Task OnTickAsync(TickContext tick, CancellationToken ct)
        {
            if (ct.IsCancellationRequested || CurrentState == SignalState.COMPLETED)
                return Task.CompletedTask;

            // Time-gate: skip ticks before entry window
            if (TimeSpan.TryParse(_config.CandleStartTime, out var startTime) &&
                DateTime.Now.TimeOfDay < startTime)
                return Task.CompletedTask;

            // Time-based squareoff: force-exit all open legs at SquareOffTime
            if (!string.IsNullOrEmpty(_config.SquareOffTime) &&
                TimeSpan.TryParse(_config.SquareOffTime, out var exitTs) &&
                DateTime.Now.TimeOfDay >= exitTs &&
                _config.Legs.Any(l => l.Status == "OPEN"))
            {
                SquareOffAll("TIME-EXIT");
                return Task.CompletedTask;
            }

            // Track whether any leg enters this tick so we set IN_POSITION after all legs are processed.
            // This fixes multi-leg: setting IN_POSITION mid-loop blocked subsequent PENDING legs.
            bool anyLegEnteredThisTick = false;

            for (int legIdx = 0; legIdx < _config.Legs.Count; legIdx++)
            {
                var leg = _config.Legs[legIdx];

                decimal spotLtp = leg.Index switch
                {
                    "BANKNIFTY"  => tick.BankNiftyLtp,
                    "FINNIFTY"   => tick.FinniftyLtp,
                    "MIDCPNIFTY" => tick.MidcpniftyLtp,
                    "SENSEX"     => tick.SensexLtp,
                    _            => tick.NiftyLtp
                };

                var chain = leg.Index switch
                {
                    "BANKNIFTY"  => tick.BankNiftyOptionChain,
                    "FINNIFTY"   => tick.FinniftyOptionChain,
                    "MIDCPNIFTY" => tick.MidcpniftyOptionChain,
                    "SENSEX"     => tick.SensexOptionChain,
                    _            => tick.NiftyOptionChain
                };

                string optType = leg.OptionType == Models.OptionType.Call ? "CE" : "PE";

                // ── ENTRY: resolve strike, fire Entry signal ────────────
                if ((leg.Status == "PENDING") && CurrentState == SignalState.WAITING)
                {
                    if (chain == null || chain.Count == 0)
                        continue;

                    // F2 + Edge-trigger: only enter on the FIRST candle conditions become true.
                    // If conditions were already true last tick, skip (no new crossing occurred).
                    if (leg.EntryConditions?.Count > 0)
                    {
                        bool currentMet = AllConditionsMet(leg.EntryConditions);
                        _prevEntryConditionsMet.TryGetValue(legIdx, out bool prevMet);
                        _prevEntryConditionsMet[legIdx] = currentMet; // update for next tick

                        if (!currentMet || prevMet) // not met, OR already was met → no crossing
                            continue;
                    }

                    int strike = leg.GetTargetStrike((double)spotLtp, chain);
                    if (strike == 0) continue;

                    var opt = chain.FirstOrDefault(c =>
                        c.Strike == strike && c.OptionType == optType);
                    if (opt == null) continue;

                    leg.CalculatedStrike = strike;
                    leg.SelectedPremium  = opt.LTP;
                    leg.SymbolToken      = opt.Token;
                    leg.TradingSymbol    = opt.Symbol ?? "";
                    leg.EntryPrice       = opt.LTP;
                    leg.Ltp              = opt.LTP;
                    leg.EntryTime        = DateTime.Now;
                    leg.EntryIndexLtp    = (double)spotLtp;
                    leg.Status           = "OPEN";
                    // Resolve lot size: option chain item first, then SEBI-mandated fallback table
                    if (opt.LotSize > 0)
                        leg.LotSize = opt.LotSize;
                    else
                    {
                        leg.LotSize = _fallbackLotSizes.TryGetValue(leg.Index?.ToUpperInvariant() ?? "", out var fb) ? fb : 1;
                        Log("WARN", $"[{Name}] LotSize=0 for {leg.Index} — using fallback {leg.LotSize}");
                    }

                    // Compute absolute SL / Target from % of entry premium (overrides fixed prices)
                    if (leg.StopLossPercent > 0)
                        leg.StopLossPrice = leg.Action == ActionType.Sell
                            ? leg.EntryPrice * (1.0 + leg.StopLossPercent / 100.0)   // sell: SL fires when premium rises X%
                            : leg.EntryPrice * (1.0 - leg.StopLossPercent / 100.0);  // buy: SL fires when premium drops X%
                    if (leg.TargetPercent > 0)
                        leg.TargetPrice = leg.Action == ActionType.Sell
                            ? leg.EntryPrice * (1.0 - leg.TargetPercent / 100.0)     // sell: profit when premium falls X%
                            : leg.EntryPrice * (1.0 + leg.TargetPercent / 100.0);    // buy: profit when premium rises X%

                    anyLegEnteredThisTick = true; // defer CurrentState change until after loop

                    // Record entry spot for UnderlyingMove trigger (F8)
                    if (_strategyEntrySpotLtp == 0)
                        _strategyEntrySpotLtp = (double)spotLtp;

                    // F1: initialise trailing stop tracker
                    _legTrailBestPrices[legIdx] = opt.LTP;

                    FireSignal(new Entities.Signal
                    {
                        StrategyId       = StrategyId,
                        LegId            = $"LEG-{StrategyId}-{legIdx:D2}",
                        SignalType       = SignalType.Entry,
                        Symbol           = leg.TradingSymbol,
                        SymbolToken      = leg.SymbolToken ?? "",
                        Price            = opt.LTP,
                        Quantity         = leg.TotalLots * ResolvedLotSize(leg),
                        TriggerCondition = $"Entry: {leg.Index} {optType} {strike} @ {opt.LTP:F2}"
                    });

                    Log("INFO", $"[{Name}] ENTRY: {optType} {strike} @ {opt.LTP:F2} lots={leg.TotalLots}");
                }

                // ── MONITOR: SL/Target check on OPEN legs ───────────────
                else if (leg.Status == "OPEN")
                {
                    // Update leg LTP from option chain if available
                    if (chain != null)
                    {
                        var opt = chain.FirstOrDefault(c =>
                            c.Strike == leg.CalculatedStrike && c.OptionType == optType);
                        if (opt != null) leg.Ltp = opt.LTP;
                    }

                    // F1: Update trailing SL before checking SL breach
                    if (leg.TrailingSL > 0 && leg.StopLossPrice > 0)
                    {
                        if (!_legTrailBestPrices.ContainsKey(legIdx))
                            _legTrailBestPrices[legIdx] = leg.Ltp;

                        double best = _legTrailBestPrices[legIdx];

                        if (leg.Action == ActionType.Sell)
                        {
                            // Sell leg: best = lowest Ltp seen. Trail stop follows down.
                            if (leg.Ltp < best)
                            {
                                best = leg.Ltp;
                                _legTrailBestPrices[legIdx] = best;
                                leg.StopLossPrice = best + leg.TrailingSL;
                                Log("INFO", $"[{Name}] Trail SL updated: {optType} {leg.CalculatedStrike} SL→{leg.StopLossPrice:F2}");
                            }
                        }
                        else
                        {
                            // Buy leg: best = highest Ltp seen. Trail stop follows up.
                            if (leg.Ltp > best)
                            {
                                best = leg.Ltp;
                                _legTrailBestPrices[legIdx] = best;
                                leg.StopLossPrice = best - leg.TrailingSL;
                                Log("INFO", $"[{Name}] Trail SL updated: {optType} {leg.CalculatedStrike} SL→{leg.StopLossPrice:F2}");
                            }
                        }
                    }

                    bool slHit = false, targetHit = false;

                    if (leg.Action == ActionType.Sell) // short: SL when price rises
                    {
                        if (leg.StopLossPrice > 0 && leg.Ltp >= leg.StopLossPrice) slHit = true;
                        if (leg.TargetPrice > 0 && leg.Ltp <= leg.TargetPrice) targetHit = true;
                    }
                    else // long: SL when price drops
                    {
                        if (leg.StopLossPrice > 0 && leg.Ltp <= leg.StopLossPrice) slHit = true;
                        if (leg.TargetPrice > 0 && leg.Ltp >= leg.TargetPrice) targetHit = true;
                    }

                    if (slHit || targetHit)
                    {
                        string reason = slHit ? "SL" : "TARGET";
                        leg.Status     = "EXITED";
                        leg.ExitPrice  = leg.Ltp;
                        leg.ExitTime   = DateTime.Now;
                        leg.ExitReason = reason;
                        _legTrailBestPrices.Remove(legIdx); // F1: clear trail state

                        FireSignal(new Entities.Signal
                        {
                            StrategyId       = StrategyId,
                            LegId            = $"LEG-{StrategyId}-{legIdx:D2}",
                            SignalType       = SignalType.Exit,
                            Symbol           = leg.TradingSymbol,
                            SymbolToken      = leg.SymbolToken ?? "",
                            Price            = leg.Ltp,
                            Quantity         = leg.TotalLots * ResolvedLotSize(leg),
                            TriggerCondition = $"{reason}: {optType} {leg.CalculatedStrike} @ {leg.Ltp:F2}"
                        });

                        Log("INFO", $"[{Name}] {reason} EXIT: {optType} {leg.CalculatedStrike} @ {leg.Ltp:F2}");

                        // If all legs exited, check if any re-entry is still possible
                        if (_config.Legs.All(l => l.Status is "EXITED" or "SQOFF"))
                        {
                            bool reEntryPossible = _config.Legs.Any(l =>
                                l.ExitReason == "SL" && l.CurrentReEntry < l.MaxReEntry);

                            CurrentState = reEntryPossible
                                ? SignalState.WAITING    // allow re-entry block to run
                                : SignalState.COMPLETED; // no more signals — strategy is done
                        }
                    }
                }

                // F8: Adjustment leg activation check (INACTIVE legs only)
                else if (leg.IsAdjustmentLeg && leg.Status == "INACTIVE")
                {
                    bool triggered = leg.AdjustmentTrigger switch
                    {
                        "ParentLegPnL"    => CheckParentLegPnLTrigger(leg, legIdx),
                        "UnderlyingMove"  => CheckUnderlyingMoveTrigger(leg, (double)spotLtp),
                        _                 => false
                    };

                    if (triggered)
                    {
                        leg.Status = "PENDING"; // entry block picks it up next tick
                        Log("INFO", $"[{Name}] Adjustment leg {legIdx} activated ({leg.AdjustmentTrigger})");
                    }
                }
            }

            // After processing all legs: if any entered this tick, move to IN_POSITION.
            // Doing this outside the loop allows ALL PENDING legs to enter in the same tick.
            if (anyLegEnteredThisTick)
                CurrentState = SignalState.IN_POSITION;

            // ── Re-entry check (after exit processing) ──────────────────
            foreach (var leg in _config.Legs)
            {
                if (leg.Status == "EXITED" && leg.ExitReason == "SL" &&
                    leg.CurrentReEntry < leg.MaxReEntry &&
                    CurrentState == SignalState.WAITING)
                {
                    int legIdx = _config.Legs.IndexOf(leg);
                    leg.CurrentReEntry++;
                    leg.Status = "PENDING"; // picked up by entry block on next tick

                    // Edge-trigger reset: mark prev as true so re-entry also waits for a
                    // fresh condition crossing (conditions must go false before firing again).
                    if (leg.EntryConditions?.Count > 0)
                        _prevEntryConditionsMet[legIdx] = true;

                    FireSignal(new Entities.Signal
                    {
                        StrategyId       = StrategyId,
                        LegId            = $"LEG-{StrategyId}-{legIdx:D2}",
                        SignalType       = SignalType.ReEntry,
                        TriggerCondition = $"Re-entry #{leg.CurrentReEntry}/{leg.MaxReEntry} after SL"
                    });

                    Log("INFO", $"[{Name}] RE-ENTRY queued: leg {legIdx}, " +
                        $"attempt {leg.CurrentReEntry}/{leg.MaxReEntry}");
                }
            }

            RecordSuccess();
            return Task.CompletedTask;
        }

        // F2: Evaluate all indicator conditions — returns true if ALL pass
        private bool AllConditionsMet(List<IndicatorCondition> conditions)
        {
            if (!_indicatorEngine.IsWarmedUp) return true; // let through if not yet warmed up

            foreach (var c in conditions)
            {
                if (!Enum.TryParse<IndicatorType>(c.IndicatorType, out var indType)) continue;
                if (!Enum.TryParse<TimeFrame>(c.TimeFrame, out var tf)) continue;

                double val = _indicatorEngine.GetValue(indType, c.Period, tf);
                if (double.IsNaN(val)) continue;

                bool pass = c.Comparator switch
                {
                    "<"  => val < c.Value,
                    ">"  => val > c.Value,
                    "<=" => val <= c.Value,
                    ">=" => val >= c.Value,
                    "==" => Math.Abs(val - c.Value) < 0.01,
                    _    => true
                };
                if (!pass) return false;
            }
            return true;
        }

        // F2: Map TimeFrame string to Angel One interval string for HistoryCacheService
        private static string TimeFrameToInterval(string tfStr) => tfStr switch
        {
            "Min1"  => "ONE_MINUTE",
            "Min3"  => "THREE_MINUTE",
            "Min5"  => "FIVE_MINUTE",
            "Min10" => "TEN_MINUTE",
            "Min15" => "FIFTEEN_MINUTE",
            "Min30" => "THIRTY_MINUTE",
            "Hour1" => "ONE_HOUR",
            "Day1"  => "ONE_DAY",
            _       => "FIFTEEN_MINUTE"
        };

        // F8: Check if parent leg P&L has crossed the trigger threshold
        private bool CheckParentLegPnLTrigger(Models.StrategyLeg adjLeg, int adjLegIdx)
        {
            if (adjLeg.ParentLegIndex < 0 || adjLeg.ParentLegIndex >= _config.Legs.Count)
                return false;

            var parent = _config.Legs[adjLeg.ParentLegIndex];
            if (parent.Status != "OPEN") return false;

            double pnl = parent.Action == ActionType.Sell
                ? (parent.EntryPrice - parent.Ltp) * parent.TotalLots
                : (parent.Ltp - parent.EntryPrice) * parent.TotalLots;

            // TriggerValue is the P&L loss threshold (negative = loss, e.g. -1000)
            return (decimal)pnl <= adjLeg.AdjustmentTriggerValue;
        }

        // F8: Check if underlying has moved by TriggerValue points from entry
        private bool CheckUnderlyingMoveTrigger(Models.StrategyLeg adjLeg, double currentSpot)
        {
            if (_strategyEntrySpotLtp == 0) return false;
            double move = Math.Abs(currentSpot - _strategyEntrySpotLtp);
            return (decimal)move >= adjLeg.AdjustmentTriggerValue;
        }

        /// <summary>Force-exit all OPEN legs (used for time-based squareoff).</summary>
        private void SquareOffAll(string reason)
        {
            for (int i = 0; i < _config.Legs.Count; i++)
            {
                var leg = _config.Legs[i];
                if (leg.Status != "OPEN") continue;

                leg.Status     = "EXITED";
                leg.ExitPrice  = leg.Ltp;
                leg.ExitTime   = DateTime.Now;
                leg.ExitReason = reason;
                _legTrailBestPrices.Remove(i);

                string optType = leg.OptionType == Models.OptionType.Call ? "CE" : "PE";
                FireSignal(new Entities.Signal
                {
                    StrategyId       = StrategyId,
                    LegId            = $"LEG-{StrategyId}-{i:D2}",
                    SignalType       = SignalType.Exit,
                    Symbol           = leg.TradingSymbol,
                    SymbolToken      = leg.SymbolToken ?? "",
                    Price            = leg.Ltp,
                    Quantity         = leg.TotalLots * ResolvedLotSize(leg),
                    TriggerCondition = $"{reason}: {optType} {leg.CalculatedStrike} @ {leg.Ltp:F2}"
                });

                Log("INFO", $"[{Name}] SQOFF ({reason}): {optType} {leg.CalculatedStrike} @ {leg.Ltp:F2}");
            }

            CurrentState = SignalState.COMPLETED;
        }
    }
}
