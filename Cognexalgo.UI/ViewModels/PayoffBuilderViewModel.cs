using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Cognexalgo.Core.Models;
using Cognexalgo.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cognexalgo.UI.ViewModels
{
    public partial class PayoffBuilderViewModel : ObservableObject
    {
        private readonly Action<HybridStrategyConfig>? _onDeploy;

        // ── Collections ───────────────────────────────────────────────────────
        public ObservableCollection<StrategyLegBuilder> Legs       { get; } = new();
        public ObservableCollection<PayoffPoint>        ProfitData { get; } = new();
        public ObservableCollection<PayoffPoint>        LossData   { get; } = new();

        // ── Dropdown sources ─────────────────────────────────────────────────
        public string[] Templates { get; } =
            { "Iron Condor", "Bull Call Spread", "Bear Put Spread",
              "Long Butterfly", "Ratio Spread (1×2)", "Custom" };

        public string[] Indices     { get; } = { "NIFTY", "BANKNIFTY", "FINNIFTY", "MIDCPNIFTY", "SENSEX" };
        public string[] ActionOpts  { get; } = { "BUY", "SELL" };
        public string[] TypeOpts    { get; } = { "CE", "PE" };

        // ── Bindable properties ───────────────────────────────────────────────
        [ObservableProperty] private string _selectedTemplate = "Iron Condor";
        [ObservableProperty] private string _selectedIndex    = "NIFTY";
        [ObservableProperty] private double _spotPrice        = 22000;
        [ObservableProperty] private string _strategyName     = "Iron Condor";

        [ObservableProperty] private string _maxProfitDisplay  = "—";
        [ObservableProperty] private string _maxLossDisplay    = "—";
        [ObservableProperty] private string _breakevenDisplay  = "—";

        [ObservableProperty] private StrategyLegBuilder? _selectedLeg;

        public PayoffBuilderViewModel(Action<HybridStrategyConfig>? onDeploy = null)
        {
            _onDeploy = onDeploy;
            LoadTemplate();
        }

        // ── Commands ──────────────────────────────────────────────────────────

        [RelayCommand]
        public void LoadTemplate()
        {
            Legs.Clear();
            double atm = Atm();

            switch (SelectedTemplate)
            {
                case "Iron Condor":
                    AddLeg("SELL", "CE", atm + 200);
                    AddLeg("BUY",  "CE", atm + 400);
                    AddLeg("SELL", "PE", atm - 200);
                    AddLeg("BUY",  "PE", atm - 400);
                    StrategyName = $"Iron Condor {atm:N0}";
                    break;

                case "Bull Call Spread":
                    AddLeg("BUY",  "CE", atm);
                    AddLeg("SELL", "CE", atm + 200);
                    StrategyName = $"Bull Call Spread {atm:N0}";
                    break;

                case "Bear Put Spread":
                    AddLeg("BUY",  "PE", atm);
                    AddLeg("SELL", "PE", atm - 200);
                    StrategyName = $"Bear Put Spread {atm:N0}";
                    break;

                case "Long Butterfly":
                    AddLeg("BUY",  "CE", atm - 200, 1);
                    AddLeg("SELL", "CE", atm,        2);
                    AddLeg("BUY",  "CE", atm + 200,  1);
                    StrategyName = $"Butterfly {atm:N0}";
                    break;

                case "Ratio Spread (1×2)":
                    AddLeg("BUY",  "CE", atm,        1);
                    AddLeg("SELL", "CE", atm + 200,  2);
                    StrategyName = $"Ratio Spread {atm:N0}";
                    break;

                default:   // Custom
                    AddLeg("SELL", "CE", atm + 200);
                    StrategyName = "Custom Strategy";
                    break;
            }

            CalculatePayoff();
        }

        [RelayCommand]
        public void AddLeg()
        {
            Legs.Add(new StrategyLegBuilder
            {
                Action     = "SELL",
                OptionType = "CE",
                Strike     = Atm() + 200,
                Lots       = 1,
                Premium    = 0,
                Index      = SelectedIndex,
                Expiry     = "Weekly"
            });
        }

        [RelayCommand]
        public void RemoveLeg(StrategyLegBuilder? leg)
        {
            if (leg != null) Legs.Remove(leg);
        }

        [RelayCommand]
        public void CalculatePayoff()
        {
            ProfitData.Clear();
            LossData.Clear();

            if (Legs.Count == 0)
            {
                MaxProfitDisplay = MaxLossDisplay = BreakevenDisplay = "—";
                return;
            }

            var result = PayoffCalculator.Calculate(Legs, SpotPrice);

            MaxProfitDisplay = result.MaxProfit > 1e8  ? "Unlimited" : $"₹{result.MaxProfit:N0}";
            MaxLossDisplay   = result.MaxLoss   < -1e8 ? "Unlimited" : $"₹{result.MaxLoss:N0}";
            BreakevenDisplay = result.Breakevens.Count > 0
                ? string.Join("  /  ", result.Breakevens.Select(b => $"₹{b:N0}"))
                : "None";

            foreach (var (price, pnl) in result.Points)
            {
                ProfitData.Add(new PayoffPoint { Price = price, Pnl = Math.Max(0, pnl) });
                LossData.Add(  new PayoffPoint { Price = price, Pnl = Math.Min(0, pnl) });
            }
        }

        [RelayCommand]
        public void DeployToEngine()
        {
            if (_onDeploy == null)
            {
                MessageBox.Show("No deploy handler is connected.", "Not Connected");
                return;
            }
            if (Legs.Count == 0)
            {
                MessageBox.Show("Add at least one leg before deploying.", "No Legs");
                return;
            }

            var config = new HybridStrategyConfig
            {
                Name         = StrategyName,
                IsActive     = true,
                ProductType  = "MIS",
                ExpiryType   = "Weekly",
                StrategyType = "Hybrid"
            };

            int atmStrike = (int)Atm();
            foreach (var bl in Legs)
            {
                int    offset    = (int)Math.Round(bl.Strike) - atmStrike;
                string atmOffset = offset == 0 ? "ATM" : $"ATM{(offset >= 0 ? "+" : "")}{offset}";

                config.Legs.Add(new StrategyLeg
                {
                    Index            = bl.Index,
                    OptionType       = bl.OptionType == "CE" ? OptionType.Call : OptionType.Put,
                    Action           = bl.Action     == "BUY" ? ActionType.Buy : ActionType.Sell,
                    TotalLots        = bl.Lots,
                    Mode             = StrikeSelectionMode.ATMPoint,
                    ATMOffset        = atmOffset,
                    CalculatedStrike = (int)Math.Round(bl.Strike),
                    EntryPrice       = bl.Premium,
                    ProductType      = "MIS",
                    ExpiryType       = "Weekly"
                });
            }

            _onDeploy(config);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private double Atm() => Math.Round(SpotPrice / 50) * 50;

        private void AddLeg(string action, string optType, double strike, int lots = 1)
        {
            Legs.Add(new StrategyLegBuilder
            {
                Action     = action,
                OptionType = optType,
                Strike     = strike,
                Lots       = lots,
                Premium    = 0,
                Index      = SelectedIndex,
                Expiry     = "Weekly"
            });
        }
    }
}
