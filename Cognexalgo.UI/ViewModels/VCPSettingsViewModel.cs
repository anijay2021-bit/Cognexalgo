using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cognexalgo.Core.Application.Interfaces;
using Cognexalgo.Core.Domain.Patterns;
using Cognexalgo.Core.Models;

namespace Cognexalgo.UI.ViewModels
{
    public partial class VCPSettingsViewModel : ObservableObject
    {
        private readonly IVCPSettingsService _settingsService;
        private readonly IVCPStrategy? _strategy;

        // ── Section 1 : Trading Mode & Status ────────────────────────────

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TradingModeDisplay))]
        [NotifyPropertyChangedFor(nameof(LiveWarningVisible))]
        private bool _isLiveMode;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusText))]
        [NotifyPropertyChangedFor(nameof(StatusColor))]
        [NotifyPropertyChangedFor(nameof(IsStrategyStopped))]
        private bool _isStrategyRunning;

        public string TradingModeDisplay => IsLiveMode ? "🔴 LIVE Trade" : "📄 Paper Trade";

        public string StatusText => IsStrategyRunning ? "Running" : "Stopped";

        public Brush StatusColor => IsStrategyRunning
            ? new SolidColorBrush(Color.FromRgb(22, 163, 74))   // green
            : new SolidColorBrush(Color.FromRgb(220, 38, 38));  // red

        public Visibility LiveWarningVisible => IsLiveMode ? Visibility.Visible : Visibility.Collapsed;

        public bool IsStrategyStopped => !IsStrategyRunning;

        // ── Section 2 : Timeframe ─────────────────────────────────────────

        [ObservableProperty]
        private VCPTimeframe _selectedTimeframe;

        public IReadOnlyList<VCPTimeframe> Timeframes { get; } = Enum.GetValues<VCPTimeframe>().ToList();

        // ── Section 3 : Risk Management ───────────────────────────────────

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MaxExposureDisplay))]
        private int _maxConcurrentTrades;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MaxExposureDisplay))]
        private decimal _riskAmountPerTrade;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ManualLotVisible))]
        private bool _useAutoLotSizing;

        [ObservableProperty]
        private int _fixedLotsPerTrade;

        public string MaxExposureDisplay =>
            $"Max Exposure: ₹{MaxConcurrentTrades * RiskAmountPerTrade:N0}";

        public Visibility ManualLotVisible =>
            UseAutoLotSizing ? Visibility.Collapsed : Visibility.Visible;

        // ── Section 4 : Exit Rules ────────────────────────────────────────

        [ObservableProperty]
        private decimal _target1RR;

        [ObservableProperty]
        private decimal _target2RR;

        [ObservableProperty]
        private int _target1ExitPercent;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PatternFailureModeVisible))]
        private bool _exitOnPatternFailure;

        [ObservableProperty]
        private ExitMode _patternFailureExitMode;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ReversalModeVisible))]
        private bool _exitOnReversalCandle;

        [ObservableProperty]
        private ExitMode _reversalCandleExitMode;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EodTimeVisible))]
        private bool _enableEndOfDaySquareOff;

        [ObservableProperty]
        private string _squareOffTimeText = "15:10";

        public Visibility PatternFailureModeVisible =>
            ExitOnPatternFailure ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ReversalModeVisible =>
            ExitOnReversalCandle ? Visibility.Visible : Visibility.Collapsed;

        public Visibility EodTimeVisible =>
            EnableEndOfDaySquareOff ? Visibility.Visible : Visibility.Collapsed;

        public IReadOnlyList<ExitMode> ExitModes { get; } = Enum.GetValues<ExitMode>().ToList();

        // ── Section 5 : Scanner Settings ─────────────────────────────────

        [ObservableProperty]
        private VCPQuality _minVCPQuality;

        [ObservableProperty]
        private ObservableCollection<string> _watchlist = new();

        [ObservableProperty]
        private string? _selectedWatchlistItem;

        [ObservableProperty]
        private string _newWatchlistSymbol = string.Empty;

        [ObservableProperty]
        private int _niftyLotSize;

        [ObservableProperty]
        private int _bankNiftyLotSize;

        public IReadOnlyList<VCPQuality> VCPQualities { get; } =
            new[] { VCPQuality.A, VCPQuality.B, VCPQuality.C };

        // ── Section 6 : Save feedback ─────────────────────────────────────

        [ObservableProperty]
        private string _saveStatusText = string.Empty;

        [ObservableProperty]
        private Visibility _saveStatusVisibility = Visibility.Collapsed;

        [ObservableProperty]
        private bool _isSaveEnabled = true;

        // ── Constructor ───────────────────────────────────────────────────

        public VCPSettingsViewModel(IVCPSettingsService settingsService, IVCPStrategy? strategy = null)
        {
            _settingsService = settingsService;
            _strategy        = strategy;
            LoadSettings();
            RefreshStrategyStatus();
        }

        // ── Load / Build ──────────────────────────────────────────────────

        private void LoadSettings()
        {
            var s = _settingsService.Load();

            IsLiveMode              = s.TradingMode == VCPTradingMode.LiveTrade;
            SelectedTimeframe       = s.Timeframe;
            MaxConcurrentTrades     = s.MaxConcurrentTrades;
            RiskAmountPerTrade      = s.RiskAmountPerTrade;
            UseAutoLotSizing        = s.UseAutoLotSizing;
            FixedLotsPerTrade       = s.FixedLotsPerTrade;
            Target1RR               = s.Target1RR;
            Target2RR               = s.Target2RR;
            Target1ExitPercent      = s.Target1ExitPercent;
            ExitOnPatternFailure    = s.ExitOnPatternFailure;
            PatternFailureExitMode  = s.PatternFailureExitMode;
            ExitOnReversalCandle    = s.ExitOnReversalCandle;
            ReversalCandleExitMode  = s.ReversalCandleExitMode;
            EnableEndOfDaySquareOff = s.EnableEndOfDaySquareOff;
            SquareOffTimeText       = s.SquareOffTime.ToString(@"hh\:mm");
            MinVCPQuality           = s.MinVCPQuality;
            NiftyLotSize            = s.NiftyLotSize;
            BankNiftyLotSize        = s.BankNiftyLotSize;

            Watchlist.Clear();
            foreach (var sym in s.Watchlist)
                Watchlist.Add(sym);
        }

        private VCPSettings BuildSettings()
        {
            TimeSpan.TryParse(SquareOffTimeText, out var squareOffTime);

            return new VCPSettings
            {
                TradingMode             = IsLiveMode ? VCPTradingMode.LiveTrade : VCPTradingMode.PaperTrade,
                Timeframe               = SelectedTimeframe,
                MaxConcurrentTrades     = MaxConcurrentTrades,
                RiskAmountPerTrade      = RiskAmountPerTrade,
                UseAutoLotSizing        = UseAutoLotSizing,
                FixedLotsPerTrade       = FixedLotsPerTrade,
                Target1RR               = Target1RR,
                Target2RR               = Target2RR,
                Target1ExitPercent      = Target1ExitPercent,
                ExitOnPatternFailure    = ExitOnPatternFailure,
                PatternFailureExitMode  = PatternFailureExitMode,
                ExitOnReversalCandle    = ExitOnReversalCandle,
                ReversalCandleExitMode  = ReversalCandleExitMode,
                EnableEndOfDaySquareOff = EnableEndOfDaySquareOff,
                SquareOffTime           = squareOffTime,
                MinVCPQuality           = MinVCPQuality,
                Watchlist               = new List<string>(Watchlist),
                NiftyLotSize            = NiftyLotSize,
                BankNiftyLotSize        = BankNiftyLotSize,
            };
        }

        // ── Commands ──────────────────────────────────────────────────────

        [RelayCommand]
        private void Save()
        {
            if (MaxConcurrentTrades < 1 || MaxConcurrentTrades > 4)
            {
                MessageBox.Show("Max Concurrent Trades must be between 1 and 4.",
                    "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (Target1RR >= Target2RR)
            {
                MessageBox.Show("Target 1 R:R must be less than Target 2 R:R.",
                    "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (Watchlist.Count == 0)
            {
                MessageBox.Show("Watchlist cannot be empty. Add at least one symbol.",
                    "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (NiftyLotSize < 1)
            {
                MessageBox.Show("NIFTY Lot Size must be at least 1.",
                    "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (BankNiftyLotSize < 1)
            {
                MessageBox.Show("BANKNIFTY Lot Size must be at least 1.",
                    "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _settingsService.Save(BuildSettings());

                SaveStatusText       = "✅ Settings saved";
                SaveStatusVisibility = Visibility.Visible;
                IsSaveEnabled        = false;

                // Re-enable Save button after 2 s (prevent double-save)
                _ = Task.Delay(2000).ContinueWith(_ =>
                    Application.Current?.Dispatcher.Invoke(() =>
                        IsSaveEnabled = true));

                // Auto-hide toast after 3 s
                _ = Task.Delay(3000).ContinueWith(_ =>
                    Application.Current?.Dispatcher.Invoke(() =>
                        SaveStatusVisibility = Visibility.Collapsed));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save settings:\n{ex.Message}",
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void ResetDefaults()
        {
            var result = MessageBox.Show(
                "Reset all VCP settings to their default values?",
                "Reset Defaults", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            // Populate fields from defaults — does NOT persist to disk
            try
            {
                var d = new VCPSettings
                {
                    Watchlist = new List<string> { "NIFTY", "BANKNIFTY" }
                };

                IsLiveMode              = d.TradingMode == VCPTradingMode.LiveTrade;
                SelectedTimeframe       = d.Timeframe;
                MaxConcurrentTrades     = d.MaxConcurrentTrades;
                RiskAmountPerTrade      = d.RiskAmountPerTrade;
                UseAutoLotSizing        = d.UseAutoLotSizing;
                FixedLotsPerTrade       = d.FixedLotsPerTrade;
                Target1RR               = d.Target1RR;
                Target2RR               = d.Target2RR;
                Target1ExitPercent      = d.Target1ExitPercent;
                ExitOnPatternFailure    = d.ExitOnPatternFailure;
                PatternFailureExitMode  = d.PatternFailureExitMode;
                ExitOnReversalCandle    = d.ExitOnReversalCandle;
                ReversalCandleExitMode  = d.ReversalCandleExitMode;
                EnableEndOfDaySquareOff = d.EnableEndOfDaySquareOff;
                SquareOffTimeText       = d.SquareOffTime.ToString(@"hh\:mm");
                MinVCPQuality           = d.MinVCPQuality;
                NiftyLotSize            = d.NiftyLotSize;
                BankNiftyLotSize        = d.BankNiftyLotSize;

                Watchlist.Clear();
                foreach (var sym in d.Watchlist)
                    Watchlist.Add(sym);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to reset defaults:\n{ex.Message}",
                    "Reset Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand(CanExecute = nameof(CanStartStrategy))]
        private async Task StartStrategy()
        {
            if (_strategy is null) { MessageBox.Show("VCP Strategy not initialized.", "Error"); return; }
            try   { await _strategy.StartAsync(CancellationToken.None); }
            catch (Exception ex)
            { MessageBox.Show($"Failed to start VCP engine:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { RefreshStrategyStatus(); }
        }

        private bool CanStartStrategy() => !IsStrategyRunning;

        [RelayCommand(CanExecute = nameof(CanStopStrategy))]
        private async Task StopStrategy()
        {
            if (_strategy is null) return;
            try   { await _strategy.StopAsync(); }
            catch (Exception ex)
            { MessageBox.Show($"Failed to stop VCP engine:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { RefreshStrategyStatus(); }
        }

        private bool CanStopStrategy() => IsStrategyRunning;

        [RelayCommand]
        private void AddWatchlistItem()
        {
            var sym = NewWatchlistSymbol.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(sym) || Watchlist.Contains(sym)) return;
            Watchlist.Add(sym);
            NewWatchlistSymbol = string.Empty;
        }

        [RelayCommand]
        private void RemoveWatchlistItem(string? symbol)
        {
            var target = symbol ?? SelectedWatchlistItem;
            if (target is null) return;

            if (Watchlist.Count <= 1)
            {
                MessageBox.Show("Watchlist must contain at least one symbol.",
                    "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Watchlist.Remove(target);
        }

        // ── Partial property hooks ────────────────────────────────────────

        partial void OnIsLiveModeChanged(bool value)
        {
            if (!value) return; // Switching to Paper — no confirmation needed

            var result = MessageBox.Show(
                "You are switching to LIVE trading.\n\nReal orders will be placed on Angel One." +
                "\nReal money is at risk.\n\nContinue?",
                "⚠️ Confirm LIVE Trading",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                // Schedule revert on next dispatcher frame to avoid re-entrance
                Application.Current?.Dispatcher.BeginInvoke(() => IsLiveMode = false);
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private void RefreshStrategyStatus()
        {
            IsStrategyRunning = _strategy?.IsRunning ?? false;
            StartStrategyCommand.NotifyCanExecuteChanged();
            StopStrategyCommand.NotifyCanExecuteChanged();
        }
    }
}
