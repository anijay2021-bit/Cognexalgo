using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cognexalgo.Core.Domain.Entities;
using Cognexalgo.Core.Domain.Enums;
using Cognexalgo.Core.Models;

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

            foreach (var leg in _config.Legs)
            {
                decimal spotLtp = leg.Index switch
                {
                    "BANKNIFTY" => tick.BankNiftyLtp,
                    "FINNIFTY"  => tick.FinniftyLtp,
                    _           => tick.NiftyLtp
                };

                var chain = leg.Index switch
                {
                    "BANKNIFTY" => tick.BankNiftyOptionChain,
                    _           => tick.NiftyOptionChain
                };

                string optType = leg.OptionType == Models.OptionType.Call ? "CE" : "PE";

                // ── ENTRY: resolve strike, fire Entry signal ────────────
                if (leg.Status == "PENDING" && CurrentState == SignalState.WAITING)
                {
                    if (chain == null || chain.Count == 0)
                        continue; // no option chain available yet

                    int strike = leg.GetTargetStrike((double)spotLtp, chain);
                    if (strike == 0) continue;

                    var opt = chain.FirstOrDefault(c =>
                        c.Strike == strike && c.OptionType == optType);
                    if (opt == null) continue;

                    leg.CalculatedStrike = strike;
                    leg.SelectedPremium  = opt.LTP;
                    leg.SymbolToken      = opt.Token;
                    leg.EntryPrice       = opt.LTP;
                    leg.Ltp              = opt.LTP;
                    leg.EntryTime        = DateTime.Now;
                    leg.EntryIndexLtp    = (double)spotLtp;
                    leg.Status           = "OPEN";
                    CurrentState         = SignalState.IN_POSITION;

                    FireSignal(new Entities.Signal
                    {
                        StrategyId       = StrategyId,
                        LegId            = $"LEG-{StrategyId}-{_config.Legs.IndexOf(leg):D2}",
                        SignalType       = SignalType.Entry,
                        Symbol           = leg.Index,
                        Price            = opt.LTP,
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

                        FireSignal(new Entities.Signal
                        {
                            StrategyId       = StrategyId,
                            LegId            = $"LEG-{StrategyId}-{_config.Legs.IndexOf(leg):D2}",
                            SignalType       = SignalType.Exit,
                            Symbol           = leg.Index,
                            Price            = leg.Ltp,
                            TriggerCondition = $"{reason}: {optType} {leg.CalculatedStrike} @ {leg.Ltp:F2}"
                        });

                        Log("INFO", $"[{Name}] {reason} EXIT: {optType} {leg.CalculatedStrike} @ {leg.Ltp:F2}");

                        // If all legs exited, reset state for potential re-entry
                        if (_config.Legs.All(l => l.Status is "EXITED" or "SQOFF"))
                            CurrentState = SignalState.WAITING;
                    }
                }
            }

            // ── Re-entry check (after exit processing) ──────────────────
            foreach (var leg in _config.Legs)
            {
                if (leg.Status == "EXITED" && leg.ExitReason == "SL" &&
                    leg.CurrentReEntry < leg.MaxReEntry &&
                    CurrentState == SignalState.WAITING)
                {
                    leg.CurrentReEntry++;
                    leg.Status = "PENDING"; // picked up by entry block on next tick

                    FireSignal(new Entities.Signal
                    {
                        StrategyId       = StrategyId,
                        LegId            = $"LEG-{StrategyId}-{_config.Legs.IndexOf(leg):D2}",
                        SignalType       = SignalType.ReEntry,
                        TriggerCondition = $"Re-entry #{leg.CurrentReEntry}/{leg.MaxReEntry} after SL"
                    });

                    Log("INFO", $"[{Name}] RE-ENTRY queued: leg {_config.Legs.IndexOf(leg)}, " +
                        $"attempt {leg.CurrentReEntry}/{leg.MaxReEntry}");
                }
            }

            RecordSuccess();
            return Task.CompletedTask;
        }
    }
}
