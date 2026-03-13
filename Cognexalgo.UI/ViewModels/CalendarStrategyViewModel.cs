using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Cognexalgo.Core;
using Cognexalgo.Core.Models;
using Cognexalgo.Core.Strategies;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cognexalgo.UI.ViewModels
{
    /// <summary>
    /// Represents a single leg row for display in the Calendar Strategy window.
    /// </summary>
    public class LegDisplayRow
    {
        public string Label        { get; set; } = "";   // "Buy Call", "Buy Put", "Sell Call", "Sell Put"
        public string Symbol       { get; set; } = "";
        public string Action       { get; set; } = "";
        public string OptionType   { get; set; } = "";
        public string Status       { get; set; } = "";
        public double EntryPrice   { get; set; }
        public double CurrentLTP   { get; set; }
        public double SLPrice      { get; set; }
        public double PnL          { get; set; }
        public bool   IsFlippedBuy { get; set; }
    }

    /// <summary>
    /// ViewModel for the Calendar Strategy configuration and live status display.
    /// Bind this to CalendarStrategyWindow.xaml.
    /// </summary>
    public partial class CalendarStrategyViewModel : ObservableObject
    {
        private readonly TradingEngine _engine;

        // ── Config Properties (bound to UI input fields) ──────────────────────

        [ObservableProperty] private string  _strategyName             = "Calendar Strategy";
        [ObservableProperty] private string  _symbol                   = "NIFTY";
        [ObservableProperty] private string  _firstEntryTime           = "09:30";
        [ObservableProperty] private int     _lots                     = 1;
        [ObservableProperty] private double  _buySLPercent             = 50.0;
        [ObservableProperty] private bool    _enableBuySLOnCandleClose = false;
        [ObservableProperty] private int     _timeframe                = 5;
        [ObservableProperty] private string  _weeklyExpiryExitTime     = "15:20";
        [ObservableProperty] private double  _maxProfit                = 10000;
        [ObservableProperty] private double  _maxLoss                  = 5000;
        [ObservableProperty] private bool    _isLiveMode               = false;

        // ── Runtime Status (updated from live strategy state) ─────────────────

        [ObservableProperty] private string  _phase          = "Not Started";
        [ObservableProperty] private double  _atmStrike;
        [ObservableProperty] private double  _unrealizedPnL;
        [ObservableProperty] private double  _realizedPnL;
        [ObservableProperty] private double  _totalPnL;
        [ObservableProperty] private string  _statusMessage  = "";
        [ObservableProperty] private double  _combinedSellEntry;

        // ── Leg Grid (4 rows: BuyCall, BuyPut, SellCall, SellPut) ────────────
        public ObservableCollection<LegDisplayRow> LegRows { get; } = new();

        // ── Event Log ─────────────────────────────────────────────────────────
        public ObservableCollection<string> EventLog { get; } = new();

        // ── Combo Options ─────────────────────────────────────────────────────
        public ObservableCollection<int>    TimeframeOptions { get; } =
            new() { 1, 3, 5, 10, 15, 30, 60 };
        public ObservableCollection<string> SymbolOptions { get; } =
            new() { "NIFTY", "BANKNIFTY", "FINNIFTY", "MIDCPNIFTY", "SENSEX" };

        private CalendarStrategy? _runningStrategy;

        private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;

        public CalendarStrategyViewModel(TradingEngine engine)
        {
            _engine = engine;

            _refreshTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _refreshTimer.Tick += (_, _) => RefreshState();
        }

        [RelayCommand]
        public void StartStrategy()
        {
            try
            {
                var config = BuildConfig();
                _runningStrategy = new CalendarStrategy(_engine, config);
                _engine.RegisterCalendarStrategy(_runningStrategy);
                _refreshTimer.Start();
                StatusMessage = $"Strategy '{config.Name}' started. " +
                                $"Waiting for entry at {config.FirstEntryTime:hh\\:mm}.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"ERROR: {ex.Message}";
            }
        }

        [RelayCommand]
        public void StopStrategy()
        {
            if (_runningStrategy == null) return;
            _runningStrategy.IsActive = false;
            _refreshTimer.Stop();
            StatusMessage = "Strategy stopped manually.";
        }

        private CalendarStrategyConfig BuildConfig()
        {
            if (!TimeSpan.TryParseExact(FirstEntryTime, @"hh\:mm",
                    null, out var entryTime))
                entryTime = new TimeSpan(9, 30, 0);

            if (!TimeSpan.TryParseExact(WeeklyExpiryExitTime, @"hh\:mm",
                    null, out var exitTime))
                exitTime = new TimeSpan(15, 20, 0);

            return new CalendarStrategyConfig
            {
                Name                     = StrategyName,
                Symbol                   = Symbol,
                FirstEntryTime           = entryTime,
                Lots                     = Lots,
                BuySLPercent             = BuySLPercent,
                EnableBuySLOnCandleClose = EnableBuySLOnCandleClose,
                Timeframe                = Timeframe,
                WeeklyExpiryExitTime     = exitTime,
                MaxProfit                = MaxProfit,
                MaxLoss                  = MaxLoss,
                IsLiveMode               = IsLiveMode
            };
        }

        private void RefreshState()
        {
            if (_runningStrategy == null) return;
            var s = _runningStrategy.State;

            Application.Current.Dispatcher.Invoke(() =>
            {
                Phase             = s.Phase.ToString();
                AtmStrike         = s.ATMStrike;
                UnrealizedPnL     = s.TotalUnrealizedPnL;
                RealizedPnL       = s.TotalRealizedPnL;
                TotalPnL          = s.TotalPnL;
                CombinedSellEntry = s.CombinedSellEntryPrice;

                RefreshLegRows(s);

                // Sync event log (newest entries only)
                var newEntries = s.EventLog.TakeLast(50).ToList();
                foreach (var entry in newEntries)
                    if (!EventLog.Contains(entry)) EventLog.Add(entry);

                if (s.Phase == CalendarPhase.Completed)
                {
                    _refreshTimer.Stop();
                    StatusMessage = $"Strategy completed. Final P&L = ₹{s.TotalPnL:F2}";
                }
            });
        }

        private void RefreshLegRows(CalendarStrategyState s)
        {
            LegRows.Clear();
            LegRows.Add(MakeLegRow("Buy Call",  s.BuyCallLeg));
            LegRows.Add(MakeLegRow("Buy Put",   s.BuyPutLeg));
            LegRows.Add(MakeLegRow("Sell Call", s.SellCallLeg));
            LegRows.Add(MakeLegRow("Sell Put",  s.SellPutLeg));
        }

        private static LegDisplayRow MakeLegRow(string label, CalendarLeg leg)
        {
            double pnl = leg.Status == "OPEN" && leg.EntryPrice > 0
                ? (leg.Action == "BUY"
                    ? (leg.CurrentLTP - leg.EntryPrice)
                    : (leg.EntryPrice - leg.CurrentLTP))
                : leg.RealizedPnL;

            return new LegDisplayRow
            {
                Label        = label,
                Symbol       = leg.TradingSymbol.Length > 0 ? leg.TradingSymbol : "-",
                Action       = leg.IsFlippedBuyLeg ? "FLIPPED-BUY" : leg.Action,
                OptionType   = leg.OptionType,
                Status       = leg.Status,
                EntryPrice   = leg.EntryPrice,
                CurrentLTP   = leg.CurrentLTP,
                SLPrice      = leg.SLPrice,
                PnL          = pnl,
                IsFlippedBuy = leg.IsFlippedBuyLeg
            };
        }
    }
}
