using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Cognexalgo.Core.Models;
using Cognexalgo.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cognexalgo.UI.ViewModels
{
    public partial class PayoffViewModel : ObservableObject
    {
        // ── All points — used for the payoff line series ──────────────────────
        public ObservableCollection<PayoffPoint> PayoffPoints { get; } = new();

        // ── Positive P&L points (clamped ≥ 0) — green fill area ──────────────
        public ObservableCollection<PayoffPoint> ProfitPoints { get; } = new();

        // ── Negative P&L points (clamped ≤ 0) — red fill area ────────────────
        public ObservableCollection<PayoffPoint> LossPoints { get; } = new();

        [ObservableProperty] private string _strategyName = string.Empty;
        [ObservableProperty] private double _spot;
        [ObservableProperty] private string _maxProfitText = "—";
        [ObservableProperty] private string _maxLossText   = "—";
        [ObservableProperty] private string _netPremiumText = "—";
        [ObservableProperty] private string _upperBeText   = "—";
        [ObservableProperty] private string _lowerBeText   = "—";

        // Annotation bindings (used by VerticalLineAnnotations in XAML)
        [ObservableProperty] private double _spotAnnotation;
        [ObservableProperty] private double _upperBeAnnotation;
        [ObservableProperty] private double _lowerBeAnnotation;

        public void Load(string name, IReadOnlyList<OptionLeg> legs, decimal spot)
        {
            StrategyName     = name;
            Spot             = (double)spot;
            SpotAnnotation   = (double)spot;

            // ── Build payoff points ──────────────────────────────────────────
            var all = PayoffEngine.Calculate(legs, spot);

            PayoffPoints.Clear();
            ProfitPoints.Clear();
            LossPoints.Clear();

            foreach (var p in all)
            {
                PayoffPoints.Add(p);
                ProfitPoints.Add(new PayoffPoint { Price = p.Price, Pnl = Math.Max(0.0, p.Pnl) });
                LossPoints.Add(  new PayoffPoint { Price = p.Price, Pnl = Math.Min(0.0, p.Pnl) });
            }

            // ── Summary stats ────────────────────────────────────────────────
            var pnls = all.Select(p => p.Pnl).ToList();
            double maxP = pnls.Max();
            double maxL = pnls.Min();

            MaxProfitText  = double.IsPositiveInfinity(maxP) ? "∞" : $"₹{maxP:N0}";
            MaxLossText    = double.IsNegativeInfinity(maxL) ? "-∞" : $"₹{maxL:N0}";

            // ── Breakeven points ─────────────────────────────────────────────
            var be = BreakEvenCalculator.Calculate(legs);

            NetPremiumText = $"₹{be.NetPremium:N0}";

            if (be.UpperBE > 0)
            {
                UpperBeText        = $"₹{be.UpperBE:N0}";
                UpperBeAnnotation  = (double)be.UpperBE;
            }
            if (be.LowerBE > 0)
            {
                LowerBeText        = $"₹{be.LowerBE:N0}";
                LowerBeAnnotation  = (double)be.LowerBE;
            }
        }
    }
}
