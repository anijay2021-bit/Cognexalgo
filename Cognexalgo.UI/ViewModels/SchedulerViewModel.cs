using System;
using System.Collections.ObjectModel;
using Cognexalgo.Core.Application.Services;
using Cognexalgo.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cognexalgo.UI.ViewModels
{
    public partial class SchedulerViewModel : ObservableObject
    {
        private readonly StrategyScheduler _scheduler;

        // ── Active schedules list ─────────────────────────────────────────────
        public ObservableCollection<ScheduledStrategyConfig> Schedules { get; } = new();

        // ── Form: core fields ─────────────────────────────────────────────────
        [ObservableProperty] private string _strategyName  = "Short Straddle";
        [ObservableProperty] private string _symbol        = "NIFTY";
        [ObservableProperty] private string _entryTimeText = "09:20";
        [ObservableProperty] private string _exitTimeText  = "15:15";
        [ObservableProperty] private double _slPercent     = 25.0;
        [ObservableProperty] private double _targetPercent = 50.0;
        [ObservableProperty] private int    _totalLots     = 1;
        [ObservableProperty] private bool   _isLiveMode    = false;

        // ── Form: strike mode ─────────────────────────────────────────────────
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsClosestPremiumMode))]
        private string _strikeMode = "ATMPoint";   // "ATMPoint" | "ClosestPremium"

        [ObservableProperty] private double _targetPremium    = 100.0;
        [ObservableProperty] private string _premiumOperator  = "~";
        [ObservableProperty] private double _premiumTolerance = 10.0;

        public bool IsClosestPremiumMode => StrikeMode == "ClosestPremium";

        // ── Selection / status ────────────────────────────────────────────────
        [ObservableProperty] private ScheduledStrategyConfig? _selectedSchedule;
        [ObservableProperty] private string _statusMessage = string.Empty;

        // ── Static options ────────────────────────────────────────────────────
        public string[] StrategyNames { get; } =
        {
            "Short Straddle", "Long Straddle",
            "Short Strangle", "Long Strangle",
            "Short Iron Condor", "Short Iron Butterfly",
            "Jade Lizard",
            "Bull Put Spread", "Bear Call Spread",
            "Bull Call Spread", "Bear Put Spread",
        };

        public string[] Symbols          { get; } = { "NIFTY", "BANKNIFTY" };
        public string[] StrikeModes      { get; } = { "ATMPoint", "ClosestPremium" };
        public string[] PremiumOperators { get; } = { "~", ">=", "<=" };

        public SchedulerViewModel(StrategyScheduler scheduler)
        {
            _scheduler = scheduler;
        }

        // ── Commands ──────────────────────────────────────────────────────────

        [RelayCommand]
        private void AddSchedule()
        {
            if (!TimeOnly.TryParse(EntryTimeText, out var entryTime))
            { StatusMessage = $"Invalid entry time '{EntryTimeText}' — use HH:mm"; return; }

            if (!TimeOnly.TryParse(ExitTimeText, out var exitTime))
            { StatusMessage = $"Invalid exit time '{ExitTimeText}' — use HH:mm"; return; }

            var config = new ScheduledStrategyConfig
            {
                StrategyName     = StrategyName,
                Symbol           = Symbol,
                EntryTime        = entryTime,
                ExitTime         = exitTime,
                SlPercent        = SlPercent,
                TargetPercent    = TargetPercent,
                TotalLots        = TotalLots,
                IsLiveMode       = IsLiveMode,
                StrikeMode       = StrikeMode,
                TargetPremium    = TargetPremium,
                PremiumOperator  = PremiumOperator,
                PremiumTolerance = PremiumTolerance,
                IsActive         = true,
            };

            _scheduler.Add(config);
            Schedules.Add(config);

            string strikeInfo = IsClosestPremiumMode
                ? $"CP {PremiumOperator} ₹{TargetPremium:N0} ±{PremiumTolerance}"
                : "ATM";
            StatusMessage = $"Scheduled: {StrategyName} ({Symbol}) @ {entryTime:HH:mm}  SL {SlPercent}%  Tgt {TargetPercent}%  Strike: {strikeInfo}";
        }

        [RelayCommand]
        private void RemoveSchedule()
        {
            if (SelectedSchedule == null) return;
            _scheduler.Remove(SelectedSchedule.Id);
            Schedules.Remove(SelectedSchedule);
            StatusMessage = "Schedule removed.";
        }

        [RelayCommand]
        private void ToggleActive()
        {
            if (SelectedSchedule == null) return;
            SelectedSchedule.IsActive = !SelectedSchedule.IsActive;
            StatusMessage = $"{SelectedSchedule.StrategyName} {(SelectedSchedule.IsActive ? "enabled" : "disabled")}.";
        }
    }
}
