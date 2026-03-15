using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using Cognexalgo.Core.Models;
using Cognexalgo.Core.Services;
using Cognexalgo.Core.Strategies;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace Cognexalgo.UI.ViewModels
{
    public partial class CalendarPerformanceDashboardViewModel : ObservableObject
    {
        private readonly CalendarStrategy          _strategy;
        private readonly CalendarPerformanceService _svc = new();

        // ── Metric summary ────────────────────────────────────────────────────
        [ObservableProperty] private int    _totalTrades;
        [ObservableProperty] private string _winRate          = "0.0%";
        [ObservableProperty] private string _maxDrawdown      = "₹0.00";
        [ObservableProperty] private string _avgWeeklyRollPnL = "₹0.00";
        [ObservableProperty] private string _totalHedgeCost   = "₹0.00";
        [ObservableProperty] private string _netHedgeBenefit  = "₹0.00";

        // ── Trade log table ───────────────────────────────────────────────────
        public ObservableCollection<CalendarPerformanceRecord> TradeLog { get; } = new();

        public string StrategyName => _strategy?.Name ?? "Calendar";

        public CalendarPerformanceDashboardViewModel(CalendarStrategy strategy)
        {
            _strategy = strategy;
            Refresh();
        }

        // ── Commands ──────────────────────────────────────────────────────────

        [RelayCommand]
        public void Refresh()
        {
            if (_strategy == null) return;

            var records = _strategy.State.PerformanceLog;
            var metrics = _svc.CalculateMetrics(records);

            TotalTrades      = metrics.TotalTrades;
            WinRate          = $"{metrics.WinRate:N1}%";
            MaxDrawdown      = $"₹{metrics.MaxDrawdown:N2}";
            AvgWeeklyRollPnL = $"₹{metrics.AvgWeeklyRollPnL:N2}";
            TotalHedgeCost   = $"₹{metrics.TotalHedgeCost:N2}";
            NetHedgeBenefit  = $"₹{metrics.NetHedgeBenefit:N2}";

            TradeLog.Clear();
            foreach (var r in records)
                TradeLog.Add(r);
        }

        [RelayCommand]
        public void ExportToCsv()
        {
            try
            {
                var dlg = new SaveFileDialog
                {
                    Title      = "Export Performance Report",
                    Filter     = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    FileName   = $"CalendarPerf_{_strategy?.Name}_{DateTime.Now:yyyyMMdd_HHmm}.csv",
                    DefaultExt = "csv"
                };

                if (dlg.ShowDialog() != true) return;

                var records = _strategy.State.PerformanceLog;
                var metrics = _svc.CalculateMetrics(records);
                _svc.SaveToCsv(records, metrics, _strategy.Name, dlg.FileName);

                MessageBox.Show(
                    $"Exported {records.Count} records to:\n{dlg.FileName}",
                    "Export Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
