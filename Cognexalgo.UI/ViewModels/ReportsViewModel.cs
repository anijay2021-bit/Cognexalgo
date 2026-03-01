using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cognexalgo.Core.Application.Interfaces;
using Cognexalgo.Core.Domain.Enums;
using Cognexalgo.Core.Infrastructure.Services;

namespace Cognexalgo.UI.ViewModels
{
    /// <summary>F3: Performance report — equity curve, Win%, Max DD, Sharpe.</summary>
    public partial class ReportsViewModel : ObservableObject
    {
        private readonly V2Bridge _v2;

        // ── Stats ──────────────────────────────────────────────────
        [ObservableProperty] private int _totalTrades;
        [ObservableProperty] private int _winningTrades;
        [ObservableProperty] private int _losingTrades;
        [ObservableProperty] private double _winPct;
        [ObservableProperty] private decimal _totalPnl;
        [ObservableProperty] private decimal _avgTradePnl;
        [ObservableProperty] private decimal _maxDrawdown;
        [ObservableProperty] private double _sharpeRatio;
        [ObservableProperty] private string _selectedRange = "Today";
        [ObservableProperty] private bool _isLoading;

        // ── Equity curve data ──────────────────────────────────────
        public ObservableCollection<EquityPoint> EquityCurve { get; } = new();

        // ── Per-strategy breakdown ─────────────────────────────────
        public ObservableCollection<StrategyReportRow> StrategyRows { get; } = new();

        public static string[] DateRanges { get; } = { "Today", "This Week", "This Month" };

        public ReportsViewModel(V2Bridge v2)
        {
            _v2 = v2;
        }

        [RelayCommand]
        public async Task LoadReport()
        {
            IsLoading = true;
            EquityCurve.Clear();
            StrategyRows.Clear();

            try
            {
                var (from, to) = SelectedRange switch
                {
                    "This Week"  => (DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek), DateTime.Now),
                    "This Month" => (new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), DateTime.Now),
                    _            => (DateTime.Today, DateTime.Now)   // Today
                };

                var orderRepo = _v2.GetService<IOrderRepository>();
                var allOrders = await orderRepo.GetByDateRangeAsync(from, to);

                // Only count filled orders with a meaningful price
                var filled = allOrders
                    .Where(o => o.Status == OrderStatus.COMPLETE && o.FilledAt.HasValue)
                    .OrderBy(o => o.FilledAt)
                    .ToList();

                TotalTrades = filled.Count;

                // Use ActualProfit if set; otherwise fall back to 0
                decimal cumPnl = 0;
                decimal peakPnl = 0;
                decimal maxDd = 0;
                var dailyPnls = new Dictionary<DateTime, decimal>();

                foreach (var o in filled)
                {
                    decimal tradePnl = o.ActualProfit;
                    cumPnl += tradePnl;

                    DateTime day = o.FilledAt!.Value.Date;
                    dailyPnls[day] = dailyPnls.GetValueOrDefault(day) + tradePnl;

                    if (cumPnl > peakPnl) peakPnl = cumPnl;
                    decimal dd = peakPnl - cumPnl;
                    if (dd > maxDd) maxDd = dd;

                    EquityCurve.Add(new EquityPoint
                    {
                        Label = o.FilledAt!.Value.ToString("dd/MM HH:mm"),
                        CumPnl = cumPnl
                    });
                }

                TotalPnl = cumPnl;
                MaxDrawdown = maxDd;
                WinningTrades = filled.Count(o => o.ActualProfit > 0);
                LosingTrades  = filled.Count(o => o.ActualProfit < 0);
                WinPct        = TotalTrades > 0 ? Math.Round((double)WinningTrades / TotalTrades * 100, 1) : 0;
                AvgTradePnl   = TotalTrades > 0 ? cumPnl / TotalTrades : 0;

                // Sharpe = (avg daily return / std dev daily return) * sqrt(252)
                if (dailyPnls.Count > 1)
                {
                    var vals = dailyPnls.Values.Select(v => (double)v).ToList();
                    double avg = vals.Average();
                    double stdDev = Math.Sqrt(vals.Sum(v => Math.Pow(v - avg, 2)) / (vals.Count - 1));
                    SharpeRatio = stdDev > 0 ? Math.Round(avg / stdDev * Math.Sqrt(252), 2) : 0;
                }
                else
                {
                    SharpeRatio = 0;
                }

                // Per-strategy breakdown
                foreach (var grp in filled.GroupBy(o => o.StrategyId))
                {
                    decimal grpPnl = grp.Sum(o => o.ActualProfit);
                    int grpWins = grp.Count(o => o.ActualProfit > 0);
                    StrategyRows.Add(new StrategyReportRow
                    {
                        StrategyId = grp.Key ?? "Unknown",
                        Trades = grp.Count(),
                        WinPct = grp.Count() > 0 ? Math.Round((double)grpWins / grp.Count() * 100, 1) : 0,
                        TotalPnl = grpPnl
                    });
                }
            }
            catch (Exception ex)
            {
                TotalPnl = 0;
                WinPct = 0;
            }
            finally
            {
                IsLoading = false;
            }
        }
    }

    public class EquityPoint
    {
        public string Label { get; set; } = "";
        public decimal CumPnl { get; set; }
    }

    public class StrategyReportRow
    {
        public string StrategyId { get; set; } = "";
        public int Trades { get; set; }
        public double WinPct { get; set; }
        public decimal TotalPnl { get; set; }
    }
}
