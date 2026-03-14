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
    /// ViewModel for CalendarStrategyWindow.
    /// Binds all config inputs, hedge parameters, start/stop, and live leg/PnL status.
    /// </summary>
    public partial class CalendarStrategyViewModel : ObservableObject
    {
        private readonly TradingEngine _engine;
        private CalendarStrategy? _running;
        private readonly System.Windows.Threading.DispatcherTimer _timer;

        // ── Config inputs ─────────────────────────────────────────────────────
        [ObservableProperty] private string _strategyName             = "Calendar Strategy";
        [ObservableProperty] private string _symbol                   = "NIFTY";
        [ObservableProperty] private string _firstEntryTime           = "09:30";
        [ObservableProperty] private int    _lots                     = 1;
        [ObservableProperty] private double _buySLPercent             = 50.0;
        [ObservableProperty] private bool   _enableBuySLOnCandleClose = false;
        [ObservableProperty] private int    _timeframe                = 5;
        [ObservableProperty] private string _weeklyExpiryExitTime     = "15:20";
        [ObservableProperty] private double _maxProfit                = 10000;
        [ObservableProperty] private double _maxLoss                  = 5000;
        [ObservableProperty] private bool   _isLiveMode               = false;

        // ── Hedge config inputs ───────────────────────────────────────────────
        [ObservableProperty] private bool _enableHedgeBuying = false;
        [ObservableProperty] private int  _hedgeStrikeOffset = 2;

        // ── Live status ───────────────────────────────────────────────────────
        [ObservableProperty] private string _phase             = "Not Started";
        [ObservableProperty] private double _atmStrike;
        [ObservableProperty] private double _combinedSellEntry;
        [ObservableProperty] private double _unrealizedPnL;
        [ObservableProperty] private double _realizedPnL;
        [ObservableProperty] private double _totalPnL;
        [ObservableProperty] private string _statusMessage     = "";
        [ObservableProperty] private bool   _isRunning         = false;
        [ObservableProperty] private bool   _hedgeActive       = false;

        // ── Dropdown options ──────────────────────────────────────────────────
        public ObservableCollection<string> SymbolOptions     { get; } =
            new() { "NIFTY", "BANKNIFTY", "FINNIFTY", "MIDCPNIFTY", "SENSEX" };
        public ObservableCollection<int>    TimeframeOptions  { get; } =
            new() { 1, 3, 5, 10, 15, 30, 60 };
        public ObservableCollection<int>    HedgeOffsetOptions { get; } =
            new() { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        // ── Leg grid + event log ──────────────────────────────────────────────
        public ObservableCollection<LegDisplayRow> LegRows  { get; } = new();
        public ObservableCollection<string>         EventLog { get; } = new();

        public CalendarStrategyViewModel(TradingEngine engine)
        {
            _engine = engine;
            _timer  = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += (_, _) => RefreshState();
        }

        // ── Commands ──────────────────────────────────────────────────────────

        [RelayCommand]
        public void StartStrategy()
        {
            try
            {
                if (!TimeSpan.TryParseExact(
                        FirstEntryTime, @"hh\:mm", null, out var entryTs))
                    entryTs = new TimeSpan(9, 30, 0);

                if (!TimeSpan.TryParseExact(
                        WeeklyExpiryExitTime, @"hh\:mm", null, out var exitTs))
                    exitTs = new TimeSpan(15, 20, 0);

                var cfg = new CalendarStrategyConfig
                {
                    Name                     = StrategyName,
                    Symbol                   = Symbol,
                    FirstEntryTime           = entryTs,
                    Lots                     = Lots,
                    BuySLPercent             = BuySLPercent,
                    EnableBuySLOnCandleClose = EnableBuySLOnCandleClose,
                    Timeframe                = Timeframe,
                    WeeklyExpiryExitTime     = exitTs,
                    MaxProfit                = MaxProfit,
                    MaxLoss                  = MaxLoss,
                    IsLiveMode               = IsLiveMode,
                    EnableHedgeBuying        = EnableHedgeBuying,
                    HedgeStrikeOffset        = HedgeStrikeOffset
                };

                _running = new CalendarStrategy(_engine, cfg);
                _engine.RegisterCalendarStrategy(_running);

                IsRunning     = true;
                StatusMessage = $"Strategy '{cfg.Name}' started. " +
                                $"Entry at {cfg.FirstEntryTime:hh\\:mm}. " +
                                (cfg.EnableHedgeBuying
                                    ? $"Hedge enabled (+{cfg.HedgeStrikeOffset} strikes)."
                                    : "Hedge disabled.");
                _timer.Start();
            }
            catch (Exception ex)
            {
                StatusMessage = $"ERROR: {ex.Message}";
            }
        }

        [RelayCommand]
        public void StopStrategy()
        {
            if (_running == null) return;
            _running.IsActive = false;
            _timer.Stop();
            IsRunning     = false;
            StatusMessage = "Strategy stopped manually.";
        }

        // ── State refresh (every 1 second) ────────────────────────────────────

        private void RefreshState()
        {
            if (_running == null) return;
            var s = _running.State;

            Application.Current.Dispatcher.Invoke(() =>
            {
                Phase             = s.Phase.ToString();
                AtmStrike         = s.ATMStrike;
                CombinedSellEntry = s.CombinedSellEntryPrice;
                UnrealizedPnL     = s.TotalUnrealizedPnL;
                RealizedPnL       = s.TotalRealizedPnL;
                TotalPnL          = s.TotalPnL;
                HedgeActive       = s.HedgeBought;

                // Rebuild leg grid
                LegRows.Clear();
                LegRows.Add(MakeRow("Buy Call (Monthly)",  s.BuyCallLeg,   false));
                LegRows.Add(MakeRow("Buy Put (Monthly)",   s.BuyPutLeg,    false));
                LegRows.Add(MakeRow("Sell Call (Weekly)",  s.SellCallLeg,  false));
                LegRows.Add(MakeRow("Sell Put (Weekly)",   s.SellPutLeg,   false));

                if (EnableHedgeBuying && s.HedgeBought)
                {
                    LegRows.Add(MakeRow("⚡ Hedge Call", s.HedgeCallLeg, true));
                    LegRows.Add(MakeRow("⚡ Hedge Put",  s.HedgePutLeg,  true));
                }

                // Append new event log entries
                foreach (var entry in s.EventLog.TakeLast(100))
                    if (!EventLog.Contains(entry)) EventLog.Add(entry);

                if (s.Phase == CalendarPhase.Completed)
                {
                    _timer.Stop();
                    IsRunning     = false;
                    StatusMessage = $"Strategy completed. P&L=₹{s.TotalPnL:F2}";
                }
            });
        }

        private static LegDisplayRow MakeRow(string label, CalendarLeg leg, bool isHedge)
        {
            double pnl = leg.Status == "OPEN" && leg.EntryPrice > 0 && leg.CurrentLTP > 0
                ? (leg.Action == "BUY"
                    ? leg.CurrentLTP - leg.EntryPrice
                    : leg.EntryPrice - leg.CurrentLTP)
                : leg.RealizedPnL;

            string displayStatus = leg.IsFlippedBuyLeg ? "FLIPPED-BUY"
                                 : leg.Status.Length > 0 ? leg.Status
                                 : "PENDING";

            return new LegDisplayRow
            {
                Leg    = label,
                Action = leg.Action.Length > 0 ? leg.Action : "-",
                Symbol = leg.TradingSymbol.Length > 0 ? leg.TradingSymbol : "-",
                Status = displayStatus,
                Entry  = leg.EntryPrice,
                Ltp    = leg.CurrentLTP,
                SL     = leg.SLPrice,
                PnL    = pnl
            };
        }
    }

    /// <summary>Row data for the leg status DataGrid.</summary>
    public class LegDisplayRow
    {
        public string Leg    { get; set; } = "";
        public string Action { get; set; } = "";
        public string Symbol { get; set; } = "";
        public string Status { get; set; } = "";
        public double Entry  { get; set; }
        public double Ltp    { get; set; }
        public double SL     { get; set; }
        public double PnL    { get; set; }
    }
}
