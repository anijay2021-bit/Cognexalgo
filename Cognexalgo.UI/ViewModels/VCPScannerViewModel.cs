using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Cognexalgo.Core.Application.Interfaces;
using Cognexalgo.Core.Models;
using Cognexalgo.UI.Models;

namespace Cognexalgo.UI.ViewModels
{
    public partial class VCPScannerViewModel : ObservableObject, IDisposable
    {
        private readonly IVCPStrategy           _strategy;
        private readonly IVCPSettingsService    _settingsService;
        private readonly IMarketDataService     _marketData;

        private readonly Dictionary<string, decimal> _lastKnownPrice = new();
        private System.Windows.Threading.DispatcherTimer? _priceRefreshTimer;
        private volatile bool _disposed;

        // ── Observable Collections ────────────────────────────────────────────

        [ObservableProperty] private ObservableCollection<VCPSignalRow> _activeSignals = new();
        [ObservableProperty] private ObservableCollection<VCPTradeRow>  _openTrades    = new();
        [ObservableProperty] private ObservableCollection<VCPTradeRow>  _tradeHistory  = new();

        // ── Stats ─────────────────────────────────────────────────────────────

        [ObservableProperty] private string  _engineStatus      = "● Stopped";
        [ObservableProperty] private string  _engineStatusColor = "#FF1744";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TotalPnLFormatted))]
        private decimal _totalPnL = 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TotalPnLFormatted))]
        private string  _totalPnLColor = "#00C853";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TotalTrades))]
        private int _winCount = 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TotalTrades))]
        private int _lossCount = 0;

        [ObservableProperty] private string _winRateDisplay      = "0%";
        [ObservableProperty] private int    _activeSignalCount   = 0;
        [ObservableProperty] private int    _openTradeCount      = 0;

        public int    TotalTrades        => WinCount + LossCount;
        public string TotalPnLFormatted  => $"₹{TotalPnL:N0}";

        // ── Constructor ───────────────────────────────────────────────────────

        public VCPScannerViewModel(
            IVCPStrategy        strategy,
            IVCPSettingsService settingsService,
            IMarketDataService  marketData)
        {
            _strategy        = strategy;
            _settingsService = settingsService;
            _marketData      = marketData;

            _strategy.OnSignalGenerated  += OnSignalGenerated;
            _strategy.OnTradeCompleted   += OnTradeCompleted;
            _marketData.OnCandleFormed   += OnCandleFormed;

            _priceRefreshTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _priceRefreshTimer.Tick += (_, _) =>
            {
                RefreshOpenTradePrices();
                RefreshSignalAges();
            };
            _priceRefreshTimer.Start();

            UpdateEngineStatus();
        }

        // ── Signal Handler ────────────────────────────────────────────────────

        private void OnSignalGenerated(object? sender, VCPSignal signal)
        {
            try
            {
                var settings = _settingsService.Load();
                var sym      = signal.Pattern.Symbol.ToUpperInvariant();
                int lotSize  = sym.Contains("BANKNIFTY") ? settings.BankNiftyLotSize
                             : sym.Contains("NIFTY")     ? settings.NiftyLotSize
                             : 1;

                var signalRow = VCPSignalRow.FromSignal(signal);
                var tradeRow  = VCPTradeRow.FromSignal(signal, lotSize);

                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        ActiveSignals.Insert(0, signalRow);
                        OpenTrades.Insert(0, tradeRow);
                        ActiveSignalCount = ActiveSignals.Count;
                        OpenTradeCount    = OpenTrades.Count;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[VCPScanner] UI update failed in OnSignalGenerated: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[VCPScanner] OnSignalGenerated error: {ex.Message}");
            }
        }

        // ── Trade Completed Handler ───────────────────────────────────────────

        private void OnTradeCompleted(object? sender, VCPTradeResult result)
        {
            try
            {
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        var trade = OpenTrades.FirstOrDefault(t => t.SignalId == result.SignalId);
                        if (trade != null) OpenTrades.Remove(trade);

                        var sig = ActiveSignals.FirstOrDefault(s => s.Symbol == result.Symbol);
                        if (sig != null) ActiveSignals.Remove(sig);

                        var historyRow = BuildHistoryRow(result, trade);
                        TradeHistory.Insert(0, historyRow);

                        UpdateStats(result);
                        OpenTradeCount    = OpenTrades.Count;
                        ActiveSignalCount = ActiveSignals.Count;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[VCPScanner] UI update failed in OnTradeCompleted: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[VCPScanner] OnTradeCompleted error: {ex.Message}");
            }
        }

        // ── Candle Handler ────────────────────────────────────────────────────

        private void OnCandleFormed(Candle candle)
        {
            try
            {
                lock (_lastKnownPrice)
                    _lastKnownPrice[candle.Symbol] = candle.Close;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[VCPScanner] OnCandleFormed error: {ex.Message}");
            }
        }

        // ── Timer Tick Helpers ────────────────────────────────────────────────

        private void RefreshOpenTradePrices()
        {
            try
            {
                foreach (var trade in OpenTrades)
                {
                    decimal price;
                    lock (_lastKnownPrice)
                    {
                        if (!_lastKnownPrice.TryGetValue(trade.Symbol, out price))
                            continue;
                    }
                    trade.UpdatePrice(price);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[VCPScanner] RefreshOpenTradePrices error: {ex.Message}");
            }
        }

        private void RefreshSignalAges()
        {
            try
            {
                var now = DateTime.Now;
                foreach (var row in ActiveSignals)
                {
                    var age = now - row.SignalTime;
                    row.SignalAge = age.TotalHours >= 1
                        ? $"{(int)age.TotalHours}h ago"
                        : $"{(int)age.TotalMinutes}m ago";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[VCPScanner] RefreshSignalAges error: {ex.Message}");
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void UpdateEngineStatus()
        {
            EngineStatus      = _strategy.IsRunning ? "● Running" : "● Stopped";
            EngineStatusColor = _strategy.IsRunning ? "#00C853"   : "#FF1744";
        }

        private void UpdateStats(VCPTradeResult result)
        {
            TotalPnL      += result.PnL;
            TotalPnLColor  = TotalPnL >= 0 ? "#00C853" : "#FF1744";

            if (result.IsWinner) WinCount++;
            else                 LossCount++;

            int total      = WinCount + LossCount;
            WinRateDisplay = total == 0 ? "0%" : $"{WinCount * 100 / total}%";
        }

        private VCPTradeRow BuildHistoryRow(VCPTradeResult result, VCPTradeRow? openTrade)
        {
            return new VCPTradeRow
            {
                SignalId           = result.SignalId,
                Symbol             = result.Symbol,
                Strike             = openTrade?.Strike    ?? string.Empty,
                EntryPrice         = result.EntryPrice,
                CurrentPrice       = result.ExitPrice,
                ExitPrice          = result.ExitPrice,
                StopLoss           = openTrade?.StopLoss  ?? 0,
                Target1            = openTrade?.Target1   ?? 0,
                Target2            = openTrade?.Target2   ?? 0,
                LotsOpen           = result.Quantity,
                LotSize            = openTrade?.LotSize   ?? 1,
                Timeframe          = openTrade?.Timeframe ?? string.Empty,
                EntryTime          = result.EntryTime,
                ExitTime           = result.ExitTime,
                Status             = "Closed",
                UnrealizedPnL      = result.PnL,
                PnLColor           = result.PnL >= 0 ? "#00C853" : "#FF1744",
                ExitTriggerDisplay = result.ExitTrigger.ToString()
            };
        }

        // ── IDisposable ───────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _strategy.OnSignalGenerated -= OnSignalGenerated;
            _strategy.OnTradeCompleted  -= OnTradeCompleted;
            _marketData.OnCandleFormed  -= OnCandleFormed;

            if (_priceRefreshTimer != null)
            {
                _priceRefreshTimer.Stop();
                _priceRefreshTimer = null;
            }

            ActiveSignals.Clear();
            OpenTrades.Clear();
            TradeHistory.Clear();
        }
    }
}
