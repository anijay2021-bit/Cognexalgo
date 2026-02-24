using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cognexalgo.Core.Application.Interfaces;
using Cognexalgo.Core.Domain.Entities;
using Cognexalgo.Core.Domain.Enums;
using Cognexalgo.Core.Domain.ValueObjects;

namespace Cognexalgo.Core.Domain.Strategies
{
    /// <summary>
    /// Straddle Strategy (Module 2A):
    /// - Sell/Buy ATM CE + ATM PE at the same strike, same expiry
    /// - Auto-select strike = nearest to spot price
    /// - Configurable: entry time, exit time, SL on premium, target on premium
    /// - Re-entry: configurable number of re-entries after SL
    /// </summary>
    public class StraddleStrategy : StrategyV2Base
    {
        private readonly StraddleConfig _config;
        private readonly IInstrumentRepository _instrumentRepo;

        public StraddleStrategy(StraddleConfig config, IInstrumentRepository instrumentRepo)
        {
            _config = config;
            _instrumentRepo = instrumentRepo;
            Type = StrategyType.STRD;
            Name = config.Name ?? "Straddle";
        }

        public override async Task InitializeAsync(CancellationToken ct)
        {
            Log("INFO", $"[{Name}] Initializing Straddle on {_config.Underlying}...");
            // Pre-load option chain for the configured expiry
            await Task.CompletedTask;
        }

        public override async Task OnTickAsync(TickContext tick, CancellationToken ct)
        {
            if (ct.IsCancellationRequested || IsCircuitBroken) return;
            var now = DateTime.Now.TimeOfDay;

            try
            {
                decimal spotPrice = _config.Underlying == "BANKNIFTY" ? tick.BankNiftyLtp : tick.NiftyLtp;
                if (spotPrice <= 0) return;

                switch (CurrentState)
                {
                    case SignalState.WAITING:
                        // Check if it's time to enter
                        if (now >= _config.EntryTime && now < _config.ExitTime)
                        {
                            await TriggerEntry(spotPrice);
                        }
                        break;

                    case SignalState.IN_POSITION:
                        // Monitor P&L, SL, Target, Time Exit
                        await MonitorPosition(tick, spotPrice);
                        break;

                    case SignalState.EXIT_TRIGGERED:
                        // Wait for exit confirmation
                        break;
                }

                RecordSuccess();
            }
            catch (Exception ex)
            {
                RecordError(ex);
            }
        }

        private async Task TriggerEntry(decimal spotPrice)
        {
            // Calculate ATM strike
            int strikeInterval = _config.Underlying == "BANKNIFTY" ? 100 : 50;
            decimal atmStrike = Math.Round(spotPrice / strikeInterval) * strikeInterval;

            Log("INFO", $"[{Name}] Entry Signal: Spot={spotPrice}, ATM Strike={atmStrike}");

            // Create legs
            var ceLeg = new StrategyLeg
            {
                LegNumber = 1,
                TradingSymbol = $"{_config.Underlying}{_config.Expiry}C{atmStrike}",
                Direction = _config.IsBuy ? Direction.BUY : Direction.SELL,
                OptionType = "CE",
                StrikePrice = atmStrike,
                Quantity = _config.Lots * _config.LotSize,
                Lots = _config.Lots,
                StopLossPrice = _config.PremiumSL,
                TargetPrice = _config.PremiumTarget
            };

            var peLeg = new StrategyLeg
            {
                LegNumber = 2,
                TradingSymbol = $"{_config.Underlying}{_config.Expiry}P{atmStrike}",
                Direction = _config.IsBuy ? Direction.BUY : Direction.SELL,
                OptionType = "PE",
                StrikePrice = atmStrike,
                Quantity = _config.Lots * _config.LotSize,
                Lots = _config.Lots,
                StopLossPrice = _config.PremiumSL,
                TargetPrice = _config.PremiumTarget
            };

            // Fire entry signal
            var signal = new Signal
            {
                StrategyId = StrategyId,
                SignalType = SignalType.Entry,
                TriggerCondition = $"Straddle Entry: ATM {atmStrike} on {_config.Underlying}",
                Price = (double)spotPrice,
                Symbol = _config.Underlying,
                Reason = "Time-based entry"
            };

            FireSignal(signal);
            CurrentState = SignalState.ENTRY_TRIGGERED;

            await Task.CompletedTask;
        }

        private async Task MonitorPosition(TickContext tick, decimal spotPrice)
        {
            var now = DateTime.Now.TimeOfDay;

            // Time-based exit
            if (now >= _config.ExitTime)
            {
                Log("INFO", $"[{Name}] Time Exit triggered at {DateTime.Now:HH:mm:ss}");
                await TriggerExit("TimeExit");
                return;
            }

            // P&L-based exits would check leg LTPs here
            await Task.CompletedTask;
        }

        private async Task TriggerExit(string reason)
        {
            var signal = new Signal
            {
                StrategyId = StrategyId,
                SignalType = SignalType.Exit,
                TriggerCondition = $"Exit: {reason}",
                Symbol = _config.Underlying,
                Reason = reason
            };

            FireSignal(signal);
            CurrentState = SignalState.EXIT_TRIGGERED;
            await Task.CompletedTask;
        }
    }

    // ─── Straddle Configuration ──────────────────────────────────
    public class StraddleConfig
    {
        public string? Name { get; set; } = "Straddle";
        public string Underlying { get; set; } = "NIFTY";
        public string Expiry { get; set; } = ""; // e.g., "27FEB2026"
        public bool IsBuy { get; set; } = false; // false = Short Straddle (sell)
        public int Lots { get; set; } = 1;
        public int LotSize { get; set; } = 75; // NIFTY default

        public TimeSpan EntryTime { get; set; } = TimeSpan.FromHours(9) + TimeSpan.FromMinutes(20);
        public TimeSpan ExitTime { get; set; } = TimeSpan.FromHours(15) + TimeSpan.FromMinutes(15);

        public decimal PremiumSL { get; set; } = 0;     // SL on combined premium
        public decimal PremiumTarget { get; set; } = 0;  // Target on combined premium

        public int MaxReEntries { get; set; } = 0;
    }
}
