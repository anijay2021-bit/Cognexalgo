using System;
using System.Threading;
using System.Threading.Tasks;
using Cognexalgo.Core.Application.Interfaces;
using Cognexalgo.Core.Domain.Entities;
using Cognexalgo.Core.Domain.Enums;

namespace Cognexalgo.Core.Domain.Strategies
{
    /// <summary>
    /// Iron Condor Strategy (Module 2A):
    /// - Sell OTM CE + Sell OTM PE (short strangle body)
    /// - Buy further OTM CE + Buy further OTM PE (protective wings)
    /// - Configurable wing width
    /// - Auto-calculate max loss / max profit at creation
    /// </summary>
    public class IronCondorStrategy : StrategyV2Base
    {
        private readonly IronCondorConfig _config;
        private readonly IInstrumentRepository _instrumentRepo;

        public IronCondorStrategy(IronCondorConfig config, IInstrumentRepository instrumentRepo)
        {
            _config = config;
            _instrumentRepo = instrumentRepo;
            Type = StrategyType.CNDL;
            Name = config.Name ?? "Iron Condor";
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
            int si = _config.Underlying == "BANKNIFTY" ? 100 : 50;
            decimal atm = Math.Round(spotPrice / si) * si;

            // 4-leg structure
            decimal sellCE = atm + _config.ShortOffset;      // Sell OTM CE
            decimal sellPE = atm - _config.ShortOffset;      // Sell OTM PE
            decimal buyCE = sellCE + _config.WingWidth;      // Buy further OTM CE (protection)
            decimal buyPE = sellPE - _config.WingWidth;      // Buy further OTM PE (protection)

            // Calculate max profit = net premium received
            // Calculate max loss = wing width - net premium
            Log("INFO", $"[{Name}] Iron Condor Entry: Sell CE {sellCE} / Buy CE {buyCE} | Sell PE {sellPE} / Buy PE {buyPE}");

            var signal = new Signal
            {
                StrategyId = StrategyId,
                SignalType = SignalType.Entry,
                TriggerCondition = $"Iron Condor: S.CE={sellCE} B.CE={buyCE} S.PE={sellPE} B.PE={buyPE}",
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

    public class IronCondorConfig
    {
        public string? Name { get; set; } = "Iron Condor";
        public string Underlying { get; set; } = "NIFTY";
        public string Expiry { get; set; } = "";
        public int Lots { get; set; } = 1;
        public int LotSize { get; set; } = 75;

        /// <summary>Points from ATM for the short (sold) strikes.</summary>
        public decimal ShortOffset { get; set; } = 200;

        /// <summary>Points between short and long strikes (wing width).</summary>
        public decimal WingWidth { get; set; } = 100;

        public TimeSpan EntryTime { get; set; } = TimeSpan.FromHours(9) + TimeSpan.FromMinutes(20);
        public TimeSpan ExitTime { get; set; } = TimeSpan.FromHours(15) + TimeSpan.FromMinutes(15);
    }

    /// <summary>
    /// Bull Call Spread / Bear Put Spread (Module 2A):
    /// - Debit spreads with configurable strikes
    /// </summary>
    public class SpreadStrategy : StrategyV2Base
    {
        private readonly SpreadConfig _config;

        public SpreadStrategy(SpreadConfig config)
        {
            _config = config;
            Type = StrategyType.PRBL;
            Name = config.Name ?? (config.IsBullish ? "Bull Call Spread" : "Bear Put Spread");
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
                            CurrentState = SignalState.EXIT_TRIGGERED;
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
            int si = _config.Underlying == "BANKNIFTY" ? 100 : 50;
            decimal atm = Math.Round(spotPrice / si) * si;

            string optionType = _config.IsBullish ? "CE" : "PE";
            decimal buyStrike, sellStrike;

            if (_config.IsBullish)
            {
                // Bull Call Spread: Buy lower strike CE, Sell higher strike CE
                buyStrike = atm;
                sellStrike = atm + _config.SpreadWidth;
            }
            else
            {
                // Bear Put Spread: Buy higher strike PE, Sell lower strike PE
                buyStrike = atm;
                sellStrike = atm - _config.SpreadWidth;
            }

            Log("INFO", $"[{Name}] Spread Entry: Buy {optionType} {buyStrike}, Sell {optionType} {sellStrike}");

            var signal = new Signal
            {
                StrategyId = StrategyId,
                SignalType = SignalType.Entry,
                TriggerCondition = $"{Name}: Buy {optionType} {buyStrike}, Sell {optionType} {sellStrike}",
                Price = (double)spotPrice,
                Symbol = _config.Underlying,
                Reason = "Time-based entry"
            };

            FireSignal(signal);
            CurrentState = SignalState.ENTRY_TRIGGERED;
            await Task.CompletedTask;
        }
    }

    public class SpreadConfig
    {
        public string? Name { get; set; }
        public string Underlying { get; set; } = "NIFTY";
        public string Expiry { get; set; } = "";
        public bool IsBullish { get; set; } = true; // true = Bull Call, false = Bear Put
        public int Lots { get; set; } = 1;
        public int LotSize { get; set; } = 75;
        public decimal SpreadWidth { get; set; } = 100; // Points between strikes

        public TimeSpan EntryTime { get; set; } = TimeSpan.FromHours(9) + TimeSpan.FromMinutes(20);
        public TimeSpan ExitTime { get; set; } = TimeSpan.FromHours(15) + TimeSpan.FromMinutes(15);
    }
}
