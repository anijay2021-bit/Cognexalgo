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
        private string _symbol;
        private string _type;
        private string _expiryType;
        private string _action;
        private string _status;
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
            var source = _strategy.MonthlyCycleLegs;

            // Sync rows: add new, update existing, preserve order
            for (int i = 0; i < source.Count; i++)
            {
                var leg = source[i];
                double current = leg.Status == "EXITED"
                    ? leg.ExitPrice
                    : (leg.Ltp > 0 ? leg.Ltp : leg.EntryPrice);

                int qty = leg.TotalLots * leg.LotSize;
                double pnl = leg.Action == ActionType.Sell
                    ? (leg.EntryPrice - current) * qty
                    : (current - leg.EntryPrice) * qty;

                if (i < Legs.Count)
                {
                    // Update existing row in-place (keeps DataGrid stable)
                    var row = Legs[i];
                    row.Symbol       = leg.TradingSymbol.Length > 0 ? leg.TradingSymbol : leg.SymbolToken;
                    row.Type         = leg.OptionType == OptionType.Call ? "CE" : "PE";
                    row.ExpiryType   = leg.ExpiryType;
                    row.Action       = leg.Action == ActionType.Buy ? "BUY" : "SELL";
                    row.Status       = leg.Status;
                    row.EntryPrice   = leg.EntryPrice;
                    row.CurrentPrice = current;
                    row.Pnl         = pnl;
                    row.EntryTime    = leg.EntryTime;
                    row.ExitTime     = leg.ExitTime;
                }
                else
                {
                    Legs.Add(new CalendarLegRow
                    {
                        Symbol       = leg.TradingSymbol.Length > 0 ? leg.TradingSymbol : leg.SymbolToken,
                        Type         = leg.OptionType == OptionType.Call ? "CE" : "PE",
                        ExpiryType   = leg.ExpiryType,
                        Action       = leg.Action == ActionType.Buy ? "BUY" : "SELL",
                        Status       = leg.Status,
                        EntryPrice   = leg.EntryPrice,
                        CurrentPrice = current,
                        Pnl         = pnl,
                        EntryTime    = leg.EntryTime,
                        ExitTime     = leg.ExitTime
                    });
                }
            }

            // Remove stale rows if cycle was reset
            while (Legs.Count > source.Count)
                Legs.RemoveAt(Legs.Count - 1);

            // Footer P&L
            var (realized, unrealized, total) = _strategy.GetMonthlyPnl();
            Realized   = realized;
            Unrealized = unrealized;
            Total      = total;
        }

        public void StopRefresh() => _timer.Stop();
    }
}
