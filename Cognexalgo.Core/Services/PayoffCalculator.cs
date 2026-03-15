using System;
using System.Collections.Generic;
using System.Linq;
using Cognexalgo.Core.Models;

namespace Cognexalgo.Core.Services
{
    public class PayoffResult
    {
        public List<(double Price, double Pnl)> Points     { get; set; } = new();
        public List<double>                      Breakevens { get; set; } = new();
        public double                            MaxProfit  { get; set; }
        public double                            MaxLoss    { get; set; }
    }

    public static class PayoffCalculator
    {
        private const int Steps = 200;

        public static PayoffResult Calculate(
            IReadOnlyList<StrategyLegBuilder> legs,
            double spotPrice)
        {
            if (legs == null || legs.Count == 0)
                return new PayoffResult();

            double lo   = spotPrice * 0.85;
            double hi   = spotPrice * 1.15;
            double step = (hi - lo) / Steps;

            var points = new List<(double Price, double Pnl)>(Steps + 1);
            for (int i = 0; i <= Steps; i++)
            {
                double price = lo + i * step;
                points.Add((price, TotalPnl(legs, price)));
            }

            return new PayoffResult
            {
                Points     = points,
                Breakevens = FindBreakevens(points),
                MaxProfit  = Math.Round(points.Max(p => p.Pnl), 2),
                MaxLoss    = Math.Round(points.Min(p => p.Pnl), 2)
            };
        }

        // ── Internals ────────────────────────────────────────────────────────

        private static double TotalPnl(IReadOnlyList<StrategyLegBuilder> legs, double spot)
        {
            double total = 0;
            foreach (var leg in legs)
            {
                double qty     = leg.Lots * LotSize(leg.Index);
                double payoff  = Intrinsic(leg.OptionType, leg.Strike, spot);
                double pnl     = leg.Action == "BUY"
                    ? (payoff - leg.Premium) * qty
                    : (leg.Premium - payoff) * qty;
                total += pnl;
            }
            return total;
        }

        private static double Intrinsic(string type, double strike, double spot)
            => type == "CE"
                ? Math.Max(0, spot - strike)
                : Math.Max(0, strike - spot);

        private static int LotSize(string index) => index switch
        {
            "BANKNIFTY"  => 15,
            "FINNIFTY"   => 40,
            "MIDCPNIFTY" => 50,
            "SENSEX"     => 10,
            _            => 75   // NIFTY default
        };

        private static List<double> FindBreakevens(List<(double Price, double Pnl)> pts)
        {
            var result = new List<double>();
            for (int i = 1; i < pts.Count; i++)
            {
                double p0 = pts[i - 1].Pnl, p1 = pts[i].Pnl;
                if (p0 * p1 < 0)
                {
                    // linear interpolation
                    double be = pts[i - 1].Price
                        + (-p0 / (p1 - p0)) * (pts[i].Price - pts[i - 1].Price);
                    result.Add(Math.Round(be, 0));
                }
                else if (Math.Abs(p1) < 1.0)
                {
                    result.Add(Math.Round(pts[i].Price, 0));
                }
            }
            // de-duplicate closely spaced points
            return result.Distinct().OrderBy(x => x).ToList();
        }
    }
}
