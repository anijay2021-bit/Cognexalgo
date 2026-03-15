using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Cognexalgo.Core.Models;
using Cognexalgo.Core.Services;
using Cognexalgo.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cognexalgo.UI.ViewModels
{
    public partial class PayoffBuilderViewModel : ObservableObject
    {
        private readonly Action<HybridStrategyConfig>? _onDeploy;
        private List<OptionChainItem> _optionChain = new();
        private string _lastTemplateName = "Iron Condor";

        // ── Payoff data collections ────────────────────────────────────────────
        public ObservableCollection<StrategyLegBuilder> Legs       { get; } = new();
        public ObservableCollection<PayoffPoint>        ProfitData { get; } = new();
        public ObservableCollection<PayoffPoint>        LossData   { get; } = new();

        // ── Left-panel strategy category collections ───────────────────────────
        public ObservableCollection<string> NeutralStrategies { get; } = new();
        public ObservableCollection<string> BullishStrategies { get; } = new();
        public ObservableCollection<string> BearishStrategies { get; } = new();

        // ── Dropdown sources ─────────────────────────────────────────────────
        public string[] Indices    { get; } = { "NIFTY", "BANKNIFTY", "FINNIFTY", "MIDCPNIFTY", "SENSEX" };
        public string[] ActionOpts { get; } = { "BUY", "SELL" };
        public string[] TypeOpts   { get; } = { "CE", "PE" };

        // ── Observable properties ─────────────────────────────────────────────
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AtmDisplay))]
        private string _selectedIndex = "NIFTY";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AtmDisplay))]
        private double _spotPrice = 22000;

        [ObservableProperty] private string _strategyName    = "Iron Condor";
        [ObservableProperty] private int    _lots            = 1;

        // ── Metrics ───────────────────────────────────────────────────────────
        [ObservableProperty] private string _maxProfitDisplay = "—";
        [ObservableProperty] private string _maxLossDisplay   = "—";
        [ObservableProperty] private string _breakevenDisplay = "—";
        [ObservableProperty] private string _netCreditDisplay = "—";
        [ObservableProperty] private string _popDisplay       = "—";

        [ObservableProperty] private StrategyLegBuilder? _selectedLeg;

        public string AtmDisplay => $"ATM: {Atm():N0}";

        // ── Constructor ───────────────────────────────────────────────────────
        public PayoffBuilderViewModel(Action<HybridStrategyConfig>? onDeploy = null)
        {
            _onDeploy = onDeploy;
            PopulateCategories();
            LoadTemplate("Iron Condor");
        }

        /// <summary>Called by MainViewModel to seed live option chain premiums.</summary>
        public void SetOptionChain(IEnumerable<OptionChainItem> chain)
        {
            _optionChain = chain.ToList();
            // Refresh premiums on current legs without rebuilding strikes
            RefreshPremiums();
            CalculatePayoff();
        }

        // ── Partial callbacks ─────────────────────────────────────────────────

        partial void OnSelectedIndexChanged(string value)
        {
            LoadTemplate(_lastTemplateName);
        }

        partial void OnSpotPriceChanged(double value)
        {
            LoadTemplate(_lastTemplateName);
        }

        // ── Commands ──────────────────────────────────────────────────────────

        /// <summary>Called by the left-panel ListBox SelectionChanged handler.</summary>
        [RelayCommand]
        public void SelectTemplate(string? name)
        {
            if (string.IsNullOrEmpty(name)) return;
            LoadTemplate(name);
        }

        /// <summary>Default (no-arg) command kept for MainViewModel.LoadTemplateCommand.Execute(null).</summary>
        [RelayCommand]
        public void LoadTemplate()
        {
            LoadTemplate(_lastTemplateName);
        }

        [RelayCommand]
        public void AddLeg()
        {
            Legs.Add(new StrategyLegBuilder
            {
                Action     = "SELL",
                OptionType = "CE",
                Strike     = Atm() + 200,
                Lots       = Lots,
                Premium    = 0,
                Index      = SelectedIndex,
                Expiry     = "Weekly"
            });
        }

        [RelayCommand]
        public void RemoveLeg(StrategyLegBuilder? leg)
        {
            if (leg != null) Legs.Remove(leg);
            CalculatePayoff();
        }

        [RelayCommand]
        public void CalculatePayoff()
        {
            ProfitData.Clear();
            LossData.Clear();

            if (Legs.Count == 0)
            {
                MaxProfitDisplay = MaxLossDisplay = BreakevenDisplay = NetCreditDisplay = PopDisplay = "—";
                return;
            }

            var result = PayoffCalculator.Calculate(Legs, SpotPrice);

            MaxProfitDisplay = result.MaxProfit >  1e8 ? "Unlimited" : $"₹{result.MaxProfit:N0}";
            MaxLossDisplay   = result.MaxLoss   < -1e8 ? "Unlimited" : $"₹{result.MaxLoss:N0}";
            BreakevenDisplay = result.Breakevens.Count > 0
                ? string.Join(" / ", result.Breakevens.Select(b => b.ToString("N0")))
                : "None";

            // Net credit = SELL premiums − BUY premiums (per lot × lot size)
            int ls = LotSizeFor(SelectedIndex);
            double netCredit = Legs.Sum(l =>
            {
                double val = l.Premium * l.Lots * ls;
                return l.Action == "SELL" ? val : -val;
            });
            NetCreditDisplay = netCredit >= 0
                ? $"₹{netCredit:N0} CR"
                : $"₹{Math.Abs(netCredit):N0} DR";

            // POP = fraction of price-range points where PnL > 0
            int profitPts = result.Points.Count(p => p.Pnl > 0);
            PopDisplay = result.Points.Count > 0
                ? $"{(double)profitPts / result.Points.Count * 100:N0}%"
                : "—";

            foreach (var (price, pnl) in result.Points)
            {
                ProfitData.Add(new PayoffPoint { Price = price, Pnl = Math.Max(0, pnl) });
                LossData.Add(  new PayoffPoint { Price = price, Pnl = Math.Min(0, pnl) });
            }
        }

        [RelayCommand]
        public void DeployPaper() => Deploy(isLive: false);

        [RelayCommand]
        public void DeployLive()
        {
            var r = MessageBox.Show(
                "Deploy as LIVE order?\nThis will place real orders with your broker.",
                "Confirm Live Deploy", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r == MessageBoxResult.Yes) Deploy(isLive: true);
        }

        [RelayCommand]
        public void ClearLegs()
        {
            Legs.Clear();
            ProfitData.Clear();
            LossData.Clear();
            MaxProfitDisplay = MaxLossDisplay = BreakevenDisplay = NetCreditDisplay = PopDisplay = "—";
            StrategyName = "Custom Strategy";
            _lastTemplateName = "Custom Strategy";
        }

        // ── Internal helpers ──────────────────────────────────────────────────

        private void PopulateCategories()
        {
            foreach (var s in StrategyRepository.GetStrategies("Neutral"))  NeutralStrategies.Add(s);
            foreach (var s in StrategyRepository.GetStrategies("Bullish"))  BullishStrategies.Add(s);
            foreach (var s in StrategyRepository.GetStrategies("Bearish"))  BearishStrategies.Add(s);
        }

        private void LoadTemplate(string name)
        {
            _lastTemplateName = name;
            var template = StrategyRepository.GetTemplate(name);
            if (template == null) return;

            double atm = Atm();
            Legs.Clear();
            StrategyName = $"{name} {(int)atm}";

            foreach (var formula in template.Legs)
            {
                double strike  = atm + formula.StrikeOffset;
                double premium = GetPremium(formula.Type, (int)strike);
                Legs.Add(new StrategyLegBuilder
                {
                    Action     = formula.Action,
                    OptionType = formula.Type,
                    Strike     = strike,
                    Lots       = Lots,
                    Premium    = premium,
                    Index      = SelectedIndex,
                    Expiry     = "Weekly"
                });
            }

            CalculatePayoff();
        }

        private void RefreshPremiums()
        {
            foreach (var leg in Legs)
            {
                double fresh = GetPremium(leg.OptionType, (int)leg.Strike);
                if (fresh > 0) leg.Premium = fresh;
            }
        }

        private void Deploy(bool isLive)
        {
            if (_onDeploy == null)
            {
                MessageBox.Show("No deploy handler connected.", "Not Connected");
                return;
            }
            if (Legs.Count == 0)
            {
                MessageBox.Show("Add at least one leg before deploying.", "No Legs");
                return;
            }

            int atmStrike = (int)Atm();
            var config = new HybridStrategyConfig
            {
                Name         = StrategyName,
                IsActive     = true,
                ProductType  = "MIS",
                ExpiryType   = "Weekly",
                StrategyType = "Hybrid",
                IsLiveMode   = isLive
            };

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
            MessageBox.Show(
                $"'{StrategyName}' deployed in {(isLive ? "LIVE" : "PAPER")} mode.",
                "Deployed", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private double Atm()
        {
            double s = SpotPrice > 0 ? SpotPrice : 22000;
            double step = StrikeStep();
            return Math.Round(s / step) * step;
        }

        private double StrikeStep() => SelectedIndex switch
        {
            "BANKNIFTY"  => 100,
            "SENSEX"     => 100,
            "MIDCPNIFTY" => 25,
            "FINNIFTY"   => 50,
            _            => 50
        };

        private static int LotSizeFor(string index) => index switch
        {
            "BANKNIFTY"  => 15,
            "FINNIFTY"   => 40,
            "MIDCPNIFTY" => 50,
            "SENSEX"     => 10,
            _            => 75
        };

        private double GetPremium(string optionType, int strike)
        {
            if (_optionChain.Count == 0) return 0;
            return _optionChain
                .FirstOrDefault(x => x.Strike == strike && x.OptionType == optionType)
                ?.LTP ?? 0;
        }
    }
}
