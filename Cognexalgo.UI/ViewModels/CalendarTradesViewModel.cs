using System;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Cognexalgo.Core.Models;
using Cognexalgo.Core.Strategies;

namespace Cognexalgo.UI.ViewModels
{
    /// <summary>One row in the calendar trades DataGrid.</summary>
    public class CalendarLegRow : ObservableObject
    {
        private string _symbol = "";
        private string _type = "";
        private string _expiryType = "";
        private string _action = "";
        private string _status = "";
        private double _entryPrice;
        private double _currentPrice;
        private double _pnl;
        private DateTime? _entryTime;
        private DateTime? _exitTime;

        public string Symbol      { get => _symbol;      set => SetProperty(ref _symbol, value); }
        public string Type        { get => _type;        set => SetProperty(ref _type, value); }
        public string ExpiryType  { get => _expiryType;  set => SetProperty(ref _expiryType, value); }
        public string Action      { get => _action;      set => SetProperty(ref _action, value); }
        public string Status      { get => _status;      set => SetProperty(ref _status, value); }
        public double EntryPrice  { get => _entryPrice;  set => SetProperty(ref _entryPrice, value); }
        public double CurrentPrice{ get => _currentPrice;set => SetProperty(ref _currentPrice, value); }
        public double Pnl         { get => _pnl;         set => SetProperty(ref _pnl, value); }
        public DateTime? EntryTime{ get => _entryTime;   set => SetProperty(ref _entryTime, value); }
        public DateTime? ExitTime { get => _exitTime;    set => SetProperty(ref _exitTime, value); }
    }

    public partial class CalendarTradesViewModel : ObservableObject
    {
        private readonly CalendarStrategy _strategy;
        private readonly DispatcherTimer _timer;

        public ObservableCollection<CalendarLegRow> Legs { get; } = new();

        [ObservableProperty] private double _realized;
        [ObservableProperty] private double _unrealized;
        [ObservableProperty] private double _total;
        [ObservableProperty] private string _title = "Calendar Trades — Monthly Cycle";

        public CalendarTradesViewModel(CalendarStrategy strategy, string strategyName)
        {
            _strategy = strategy;
            Title = $"{strategyName} — Monthly Cycle Trades";

            Refresh();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += (_, __) => Refresh();
            _timer.Start();
        }

        private void Refresh()
        {
            var s = _strategy.State;

            // Build display rows from the 4 fixed legs in the current strategy state
            var legDefs = new (string Label, CalendarLeg Leg)[]
            {
                ("Buy Call",  s.BuyCallLeg),
                ("Buy Put",   s.BuyPutLeg),
                ("Sell Call", s.SellCallLeg),
                ("Sell Put",  s.SellPutLeg),
            };

            for (int i = 0; i < legDefs.Length; i++)
            {
                var (label, leg) = legDefs[i];
                double current = leg.CurrentLTP > 0 ? leg.CurrentLTP : leg.EntryPrice;
                string expiryType = leg.IsWeekly ? "Weekly" : "Monthly";

                if (i < Legs.Count)
                {
                    var row = Legs[i];
                    row.Symbol       = leg.TradingSymbol.Length > 0 ? leg.TradingSymbol : label;
                    row.Type         = leg.OptionType;
                    row.ExpiryType   = expiryType;
                    row.Action       = leg.IsFlippedBuyLeg ? "FLIPPED-BUY" : leg.Action;
                    row.Status       = leg.Status;
                    row.EntryPrice   = leg.EntryPrice;
                    row.CurrentPrice = current;
                    row.Pnl          = leg.UnrealizedPnL + leg.RealizedPnL;
                }
                else
                {
                    Legs.Add(new CalendarLegRow
                    {
                        Symbol       = leg.TradingSymbol.Length > 0 ? leg.TradingSymbol : label,
                        Type         = leg.OptionType,
                        ExpiryType   = expiryType,
                        Action       = leg.IsFlippedBuyLeg ? "FLIPPED-BUY" : leg.Action,
                        Status       = leg.Status,
                        EntryPrice   = leg.EntryPrice,
                        CurrentPrice = current,
                        Pnl          = leg.UnrealizedPnL + leg.RealizedPnL,
                    });
                }
            }

            // Footer P&L from strategy state
            Realized   = s.TotalRealizedPnL;
            Unrealized = s.TotalUnrealizedPnL;
            Total      = s.TotalPnL;
        }

        public void StopRefresh() => _timer.Stop();
    }
}
