using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cognexalgo.Core.Application.Interfaces;
using Cognexalgo.Core.Domain.Entities;
using Cognexalgo.Core.Domain.Enums;

namespace Cognexalgo.Core.Domain.Strategies
{
    /// <summary>
    /// Strangle Strategy (Module 2A):
    /// - Buy/Sell OTM CE + OTM PE with configurable offset from ATM
    /// - Example: "50 points away from ATM" or "1 strike away"
    /// - Auto-select strikes from live option chain
    /// </summary>
    public class StrangleStrategy : StrategyV2Base
    {
        private readonly StrangleConfig _config;
        private readonly IInstrumentRepository _instrumentRepo;

        public StrangleStrategy(StrangleConfig config, IInstrumentRepository instrumentRepo)
        {
            _config = config;
            _instrumentRepo = instrumentRepo;
            Type = StrategyType.STNG;
            Name = config.Name ?? "Strangle";
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
                        if (now >= _config.EntryTime && now < _config.ExitTime)
                        {
                            await TriggerEntry(spotPrice);
                        }
                        break;

                    case SignalState.IN_POSITION:
                        if (now >= _config.ExitTime)
                        {
                            await TriggerExit("TimeExit");
                        }
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
            int strikeInterval = _config.Underlying == "BANKNIFTY" ? 100 : 50;
            decimal atmStrike = Math.Round(spotPrice / strikeInterval) * strikeInterval;

            // OTM strikes: CE above ATM, PE below ATM
            decimal ceStrike = atmStrike + (_config.OffsetPoints > 0 
                ? _config.OffsetPoints 
                : strikeInterval * _config.OffsetStrikes);
            decimal peStrike = atmStrike - (_config.OffsetPoints > 0 
                ? _config.OffsetPoints 
                : strikeInterval * _config.OffsetStrikes);

            Log("INFO", $"[{Name}] Strangle Entry: CE={ceStrike}, PE={peStrike}, Spot={spotPrice}");

            var signal = new Signal
            {
                StrategyId = StrategyId,
                SignalType = SignalType.Entry,
                TriggerCondition = $"Strangle: OTM CE {ceStrike} + OTM PE {peStrike}",
                Price = (double)spotPrice,
                Symbol = _config.Underlying,
                Reason = "Time-based entry"
            };

            FireSignal(signal);
            CurrentState = SignalState.ENTRY_TRIGGERED;
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

    public class StrangleConfig
    {
        public string? Name { get; set; } = "Strangle";
        public string Underlying { get; set; } = "NIFTY";
        public string Expiry { get; set; } = "";
        public bool IsBuy { get; set; } = false;
        public int Lots { get; set; } = 1;
        public int LotSize { get; set; } = 75;

        /// <summary>Points away from ATM (e.g., 50, 100). Takes priority over OffsetStrikes.</summary>
        public decimal OffsetPoints { get; set; } = 100;

        /// <summary>Number of strikes away (e.g., 1 = one strike interval).</summary>
        public int OffsetStrikes { get; set; } = 0;

        public TimeSpan EntryTime { get; set; } = TimeSpan.FromHours(9) + TimeSpan.FromMinutes(20);
        public TimeSpan ExitTime { get; set; } = TimeSpan.FromHours(15) + TimeSpan.FromMinutes(15);
        public decimal PremiumSL { get; set; }
        public decimal PremiumTarget { get; set; }
    }
}
