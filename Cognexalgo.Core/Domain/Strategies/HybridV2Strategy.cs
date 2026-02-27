using System;
using System.Threading;
using System.Threading.Tasks;
using Cognexalgo.Core.Domain.Enums;
using Cognexalgo.Core.Models;

namespace Cognexalgo.Core.Domain.Strategies
{
    /// <summary>
    /// Thin StrategyV2Base wrapper for HybridStrategyConfig.
    /// Allows any legacy hybrid strategy to be registered with the V2 Orchestrator
    /// for start/stop lifecycle management, RMS monitoring and signal routing.
    /// The actual trade execution logic is delegated to the existing TradingEngine;
    /// this class provides the Orchestrator-compatible interface.
    /// </summary>
    public class HybridV2Strategy : StrategyV2Base
    {
        private readonly HybridStrategyConfig _config;

        public HybridV2Strategy(HybridStrategyConfig config)
        {
            _config    = config ?? throw new ArgumentNullException(nameof(config));

            // Use persisted V2Id if available; otherwise generate a deterministic fallback
            StrategyId  = !string.IsNullOrEmpty(config.V2Id)
                          ? config.V2Id
                          : $"HYB-{config.Id}-{DateTime.Now:yyyyMMdd}";

            Name        = config.Name ?? "HybridStrategy";
            Type        = StrategyType.CSTM;
            TradingMode = TradingMode.PaperTrade;
        }

        /// <summary>
        /// Called by the Orchestrator on every market tick.
        /// Time-gates execution to after CandleStartTime; all heavy RMS/P&L work is
        /// handled by StrategyRmsService in the V2Bridge layer.
        /// </summary>
        public override Task OnTickAsync(TickContext tick, CancellationToken ct)
        {
            if (ct.IsCancellationRequested || CurrentState == SignalState.COMPLETED)
                return Task.CompletedTask;

            // Time-gate: skip ticks that arrive before the configured entry window
            if (TimeSpan.TryParse(_config.CandleStartTime, out var startTime) &&
                DateTime.Now.TimeOfDay < startTime)
                return Task.CompletedTask;

            // Log presence — real entry/exit signals come from TradingEngine strategy rules
            Log("INFO", $"[{Name}] tick — N={tick.NiftyLtp:F0} BN={tick.BankNiftyLtp:F0}");

            RecordSuccess();
            return Task.CompletedTask;
        }
    }
}
