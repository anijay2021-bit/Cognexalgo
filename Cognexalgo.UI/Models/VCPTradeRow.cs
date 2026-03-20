using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Cognexalgo.Core.Models;

namespace Cognexalgo.UI.Models
{
    public partial class VCPTradeRow : ObservableObject
    {
        [ObservableProperty] private Guid    _signalId;
        [ObservableProperty] private string  _symbol               = string.Empty;
        [ObservableProperty] private string  _strike               = string.Empty;
        [ObservableProperty] private decimal _entryPrice;
        [ObservableProperty] private decimal _currentPrice;
        [ObservableProperty] private decimal _exitPrice;
        [ObservableProperty] private decimal _stopLoss;
        [ObservableProperty] private decimal _target1;
        [ObservableProperty] private decimal _target2;
        [ObservableProperty] private int     _lotsOpen;
        [ObservableProperty] private decimal _unrealizedPnL;
        [ObservableProperty] private string  _pnLColor             = "#00C853";
        [ObservableProperty] private double  _progressValue;
        [ObservableProperty] private string  _status               = "Open";
        [ObservableProperty] private DateTime _entryTime;
        [ObservableProperty] private DateTime _exitTime;
        [ObservableProperty] private string  _timeframe            = string.Empty;
        [ObservableProperty] private int     _lotSize;
        [ObservableProperty] private string  _exitTriggerDisplay   = string.Empty;

        /// <summary>
        /// Updates CurrentPrice and recalculates PnL, color, and progress.
        /// Safe to call from the UI thread (DispatcherTimer) or after Dispatcher.InvokeAsync.
        /// </summary>
        public void UpdatePrice(decimal newPrice)
        {
            CurrentPrice  = newPrice;
            UnrealizedPnL = (CurrentPrice - EntryPrice) * LotsOpen * LotSize;
            PnLColor      = UnrealizedPnL >= 0 ? "#00C853" : "#FF1744";

            decimal range = Target2 - StopLoss;
            ProgressValue = range <= 0
                ? 0
                : Math.Clamp((double)((newPrice - StopLoss) / range * 100), 0, 100);
        }

        /// <summary>Creates an open trade display row from a freshly received VCP signal.</summary>
        public static VCPTradeRow FromSignal(VCPSignal signal, int lotSize)
        {
            var row = new VCPTradeRow
            {
                SignalId    = signal.Id,
                Symbol      = signal.Pattern.Symbol,
                Strike      = signal.SuggestedStrike,
                EntryPrice  = signal.EntryPrice,
                CurrentPrice= signal.EntryPrice,
                StopLoss    = signal.StopLoss,
                Target1     = signal.Target1,
                Target2     = signal.Target2,
                LotsOpen    = 1,
                LotSize     = lotSize,
                Timeframe   = signal.Pattern.Timeframe,
                EntryTime   = signal.SignalTime,
                Status      = "Open",
                PnLColor    = "#00C853"
            };
            row.UpdatePrice(signal.EntryPrice);
            return row;
        }
    }
}
