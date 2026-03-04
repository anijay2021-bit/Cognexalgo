using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace Cognexalgo.UI
{
    // ─── Commands ───────────────────────────────────────────────────────────────
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        public RelayCommand(Action<object?> execute) => _execute = execute;
        public bool CanExecute(object? p) => true;
        public void Execute(object? p) => _execute(p);
        public event EventHandler? CanExecuteChanged;
    }

    // ─── Data models ────────────────────────────────────────────────────────────
    public class StrategyItem
    {
        public int     Id                 { get; set; }
        public string  Name               { get; set; } = "";
        public string  InstrumentType     { get; set; } = "";
        public string  Status             { get; set; } = "";
        public string  TradingModeDisplay { get; set; } = "";
        public decimal Pnl                { get; set; }
        public decimal Ltp                { get; set; }
        public DateTime EntryTime         { get; set; }
        public DateTime ExitTime          { get; set; }
    }

    public class PositionItem
    {
        public string  TradingSymbol { get; set; } = "";
        public int     NetQty        { get; set; }
        public decimal Pnl          { get; set; }
        public string  Status       { get; set; } = "";
        public decimal Ltp          { get; set; }
        public decimal AvgNetPrice  { get; set; }
        public decimal StopLoss     { get; set; }
        public decimal Target       { get; set; }
        public double  IV           { get; set; }
        public double  Delta        { get; set; }
        public double  Theta        { get; set; }
        public double  Vega         { get; set; }
    }

    public class OrderItem
    {
        public DateTime Timestamp       { get; set; }
        public string   OrderId         { get; set; } = "";
        public string   TransactionType { get; set; } = "";
        public string   Symbol          { get; set; } = "";
        public int      Qty             { get; set; }
        public decimal  Price           { get; set; }
        public string   Status          { get; set; } = "";
        public string   StrategyName    { get; set; } = "";
    }

    public class OptionChainItem
    {
        public int    Strike       { get; set; }
        public string OptionType   { get; set; } = "";
        public double LTP          { get; set; }
        public double IV           { get; set; }
        public double Delta        { get; set; }
        public double Gamma        { get; set; }
        public double Theta        { get; set; }
        public double Vega         { get; set; }
        public int    DaysToExpiry { get; set; }
        public string Symbol       { get; set; } = "";
    }

    public class PnlPoint
    {
        public DateTime Time  { get; set; }
        public double   Value { get; set; }
    }

    public class AccountItem
    {
        public string  AccountName    { get; set; } = "";
        public string  Broker         { get; set; } = "";
        public string  ClientId       { get; set; } = "";
        public string  Status         { get; set; } = "";
        public decimal Pnl            { get; set; }
        public decimal FundsAvailable { get; set; }
        public int     PositionOpen   { get; set; }
        public int     OrderCompl     { get; set; }
        // Design1 column name
        public int     OrderTotal     { get; set; }
    }

    public class AccountManagerProxy : INotifyPropertyChanged
    {
        public ObservableCollection<AccountItem> Accounts { get; } = new()
        {
            new() { AccountName = "Anijay - Main",    Broker = "Angel One", ClientId = "A123456", Status = "ACTIVE", Pnl = 18750m, FundsAvailable = 485000m, PositionOpen = 6, OrderCompl = 10, OrderTotal = 10 },
            new() { AccountName = "Anijay - Hedge",   Broker = "Angel One", ClientId = "A123457", Status = "ACTIVE", Pnl = -1500m, FundsAvailable = 150000m, PositionOpen = 2, OrderCompl =  4, OrderTotal =  4 },
        };
        public bool           IsAdminMode             { get; } = true;
        public ICommand       OpenAddAccountCommand   { get; } = new RelayCommand(_ => { });
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class ClosedTradeItem
    {
        public string  StrategyName    { get; set; } = "";
        public string  Symbol          { get; set; } = "";
        public decimal PotentialProfit { get; set; }
        public decimal ProtectedProfit { get; set; }
        public decimal ActualProfit    { get; set; }
    }

    // ─── ViewModel ──────────────────────────────────────────────────────────────
    public class PreviewViewModel : INotifyPropertyChanged
    {
        private readonly DispatcherTimer _timer = new();
        private string _marketTime = "";
        private decimal _totalMtm = 18750m;

        public PreviewViewModel()
        {
            Strategies = new ObservableCollection<StrategyItem>
            {
                new() { Id = 1, Name = "Iron Condor NIFTY",        InstrumentType = "Options", Status = "RUNNING", TradingModeDisplay = "PAPER", Pnl =  8750m, Ltp = 22485.60m, EntryTime = DateTime.Today.AddHours(9).AddMinutes(20) },
                new() { Id = 2, Name = "Short Straddle BANKNIFTY", InstrumentType = "Options", Status = "RUNNING", TradingModeDisplay = "PAPER", Pnl =  5200m, Ltp = 47830.40m, EntryTime = DateTime.Today.AddHours(9).AddMinutes(35) },
                new() { Id = 3, Name = "Bull Put Spread FINNIFTY",  InstrumentType = "Options", Status = "RUNNING", TradingModeDisplay = "PAPER", Pnl =  2800m, Ltp = 21650.25m, EntryTime = DateTime.Today.AddHours(10).AddMinutes(5) },
                new() { Id = 4, Name = "Bear Call NIFTY Weekly",    InstrumentType = "Options", Status = "EXITED",  TradingModeDisplay = "LIVE",  Pnl = -1500m, Ltp =      0m,  EntryTime = DateTime.Today.AddHours(9).AddMinutes(45), ExitTime = DateTime.Today.AddHours(11).AddMinutes(10) },
            };

            Positions = new ObservableCollection<PositionItem>
            {
                new() { TradingSymbol = "NIFTY26MAR22500CE",      NetQty = -50, Pnl =  4200m, Status = "OPEN", Ltp =  145.50m, AvgNetPrice =  229.50m, StopLoss = 300m, Target =  50m, IV = 14.2, Delta = -0.318, Theta =  -8.4, Vega = 42.1 },
                new() { TradingSymbol = "NIFTY26MAR22000PE",      NetQty = -50, Pnl =  3100m, Status = "OPEN", Ltp =   88.30m, AvgNetPrice =  150.30m, StopLoss = 200m, Target =  30m, IV = 16.8, Delta =  0.224, Theta =  -6.2, Vega = 35.8 },
                new() { TradingSymbol = "BANKNIFTY27MAR48000CE",  NetQty = -30, Pnl =  2900m, Status = "OPEN", Ltp =  312.70m, AvgNetPrice =  409.40m, StopLoss = 550m, Target = 100m, IV = 18.5, Delta = -0.385, Theta = -15.3, Vega = 78.2 },
                new() { TradingSymbol = "BANKNIFTY27MAR47000PE",  NetQty = -30, Pnl =  2200m, Status = "OPEN", Ltp =  198.40m, AvgNetPrice =  271.80m, StopLoss = 350m, Target =  70m, IV = 19.2, Delta =  0.291, Theta = -13.7, Vega = 71.4 },
                new() { TradingSymbol = "FINNIFTY25MAR22000CE",   NetQty = -25, Pnl =  1400m, Status = "OPEN", Ltp =   83.20m, AvgNetPrice =  139.20m, StopLoss = 190m, Target =  25m, IV = 12.6, Delta = -0.267, Theta =  -5.8, Vega = 28.9 },
                new() { TradingSymbol = "FINNIFTY25MAR21000PE",   NetQty = -25, Pnl =  1400m, Status = "OPEN", Ltp =   71.50m, AvgNetPrice =  127.50m, StopLoss = 175m, Target =  25m, IV = 13.4, Delta =  0.241, Theta =  -5.2, Vega = 26.3 },
            };

            Orders = new ObservableCollection<OrderItem>
            {
                new() { Timestamp = DateTime.Today.AddHours(9).AddMinutes(20), OrderId = "OD0001234", TransactionType = "SELL", Symbol = "NIFTY26MAR22500CE",     Qty = 50, Price =  229.50m, Status = "COMPLETE", StrategyName = "Iron Condor NIFTY" },
                new() { Timestamp = DateTime.Today.AddHours(9).AddMinutes(20), OrderId = "OD0001235", TransactionType = "SELL", Symbol = "NIFTY26MAR22000PE",     Qty = 50, Price =  150.30m, Status = "COMPLETE", StrategyName = "Iron Condor NIFTY" },
                new() { Timestamp = DateTime.Today.AddHours(9).AddMinutes(35), OrderId = "OD0001236", TransactionType = "SELL", Symbol = "BANKNIFTY27MAR48000CE", Qty = 30, Price =  409.40m, Status = "COMPLETE", StrategyName = "Short Straddle BANKNIFTY" },
                new() { Timestamp = DateTime.Today.AddHours(9).AddMinutes(35), OrderId = "OD0001237", TransactionType = "SELL", Symbol = "BANKNIFTY27MAR47000PE", Qty = 30, Price =  271.80m, Status = "COMPLETE", StrategyName = "Short Straddle BANKNIFTY" },
                new() { Timestamp = DateTime.Today.AddHours(10).AddMinutes(5), OrderId = "OD0001238", TransactionType = "SELL", Symbol = "FINNIFTY25MAR22000CE",  Qty = 25, Price =  139.20m, Status = "COMPLETE", StrategyName = "Bull Put Spread FINNIFTY" },
                new() { Timestamp = DateTime.Today.AddHours(10).AddMinutes(5), OrderId = "OD0001239", TransactionType = "SELL", Symbol = "FINNIFTY25MAR21000PE",  Qty = 25, Price =  127.50m, Status = "PENDING",  StrategyName = "Bull Put Spread FINNIFTY" },
            };

            OptionChain = new ObservableCollection<OptionChainItem>();
            int[] strikes = { 21800, 21900, 22000, 22100, 22200, 22300, 22400, 22500, 22600, 22700, 22800, 22900, 23000 };
            double spot = 22485.6;
            foreach (var strike in strikes)
            {
                double d = (strike - spot) / spot;
                OptionChain.Add(new OptionChainItem { Strike = strike, OptionType = "CE", LTP = Math.Max(5, 200 - d * 2000), IV = 14 + Math.Abs(d) * 100, Delta = Math.Round(Math.Max(-1, Math.Min(0, -0.5 + d * 3)), 3), Gamma = 0.0012, Theta = -8.5, Vega = 45.2, DaysToExpiry = 22, Symbol = $"NIFTY26MAR{strike}CE" });
                OptionChain.Add(new OptionChainItem { Strike = strike, OptionType = "PE", LTP = Math.Max(5, 200 + d * 2000), IV = 16 + Math.Abs(d) * 100, Delta = Math.Round(Math.Max(0,  Math.Min(1,  0.5 - d * 3)), 3), Gamma = 0.0012, Theta = -7.8, Vega = 43.1, DaysToExpiry = 22, Symbol = $"NIFTY26MAR{strike}PE" });
            }

            PnlHistory = new ObservableCollection<PnlPoint>();
            var t = DateTime.Today.AddHours(9).AddMinutes(15);
            var rng = new Random(42);
            double runningPnl = 0;
            while (t <= DateTime.Today.AddHours(14).AddMinutes(45))
            {
                runningPnl += rng.NextDouble() * 900 - 250;
                PnlHistory.Add(new PnlPoint { Time = t, Value = runningPnl });
                t = t.AddMinutes(5);
            }

            LogEntries = new ObservableCollection<string>
            {
                "[09:15:02] INFO   System started. Market hours: 09:15 – 15:30",
                "[09:15:04] INFO   Angel One WebSocket connected. Instruments subscribed: 26",
                "[09:20:11] TRADE  Iron Condor NIFTY — ENTRY | NIFTY26MAR22500CE SELL 50 @ ₹229.50",
                "[09:20:11] TRADE  Iron Condor NIFTY — ENTRY | NIFTY26MAR22000PE SELL 50 @ ₹150.30",
                "[09:35:22] TRADE  Short Straddle BANKNIFTY — ENTRY | BANKNIFTY27MAR48000CE SELL 30 @ ₹409.40",
                "[09:35:22] TRADE  Short Straddle BANKNIFTY — ENTRY | BANKNIFTY27MAR47000PE SELL 30 @ ₹271.80",
                "[10:05:44] TRADE  Bull Put Spread FINNIFTY — ENTRY | FINNIFTY25MAR22000CE SELL 25 @ ₹139.20",
                "[10:05:44] TRADE  Bull Put Spread FINNIFTY — ENTRY | FINNIFTY25MAR21000PE SELL 25 @ ₹127.50",
                "[10:12:08] INFO   Greeks updated. NIFTY IV: 14.2%  BANKNIFTY IV: 18.5%",
                "[10:45:31] RMS    MTM: ₹18,750 | Max DD: ₹4,200 | All strategies within limits",
                "[11:02:17] INFO   Candle cache synced. NIFTY 5m: 62 bars  BANKNIFTY 5m: 62 bars",
                "[13:30:00] INFO   Mid-session: Portfolio MTM ₹18,750 | Open legs: 6",
            };

            ClosedTradeAnalytics = new ObservableCollection<ClosedTradeItem>
            {
                new() { StrategyName = "Bear Call NIFTY Weekly",    Symbol = "NIFTY26MAR23000CE", PotentialProfit = 3200m, ProtectedProfit = 2800m, ActualProfit = 2650m },
                new() { StrategyName = "Iron Condor NIFTY",         Symbol = "NIFTY26MAR22500CE", PotentialProfit = 9800m, ProtectedProfit = 8750m, ActualProfit = 8750m },
            };

            AccountManager = new AccountManagerProxy();

            // Live clock
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += (_, _) =>
            {
                MarketTime = DateTime.Now.ToString("HH:mm:ss");
                TotalMtm   = _totalMtm + (decimal)(new Random().NextDouble() * 100 - 50);
            };
            _timer.Start();
            MarketTime = DateTime.Now.ToString("HH:mm:ss");
        }

        // ── Properties ───────────────────────────────────────────────────────
        public ObservableCollection<StrategyItem>    Strategies          { get; }
        public ObservableCollection<PositionItem>    Positions           { get; }
        public ObservableCollection<OrderItem>       Orders              { get; }
        public ObservableCollection<OptionChainItem> OptionChain         { get; }
        public ObservableCollection<PnlPoint>        PnlHistory          { get; }
        public ObservableCollection<string>          LogEntries          { get; }
        public ObservableCollection<ClosedTradeItem> ClosedTradeAnalytics{ get; }
        public AccountManagerProxy                   AccountManager      { get; }

        public decimal TotalMtm
        {
            get => _totalMtm;
            set { _totalMtm = value; OnPropertyChanged(); }
        }

        public int     RunningStrategiesCount { get; } = 3;
        public int     PendingOrdersCount     { get; } = 1;
        public int     ExitedStrategiesCount  { get; } = 1;
        public decimal MaxDrawdown            { get; } = 4200m;
        public decimal TotalV2Pnl            { get; } = 15250m;
        public decimal SpotPrice             { get; } = 22485.60m;
        public string  SelectedOptionIndex   { get; set; } = "NIFTY";
        public string  ConnectionStatus      { get; } = "Connected";
        public string  CurrentPage           { get; } = "Dashboard";
        public int     SelectedTabIndex      { get; set; } = 0;

        // Design2 analytics
        public double  ProfitEfficiency { get; } = 87.5;
        public decimal RescuedCapital   { get; } = 12400m;
        public decimal AvgSlippage      { get; } = 85m;

        // Clock (updated by timer)
        private string _clock = "";
        public string MarketTime
        {
            get => _clock;
            set { _clock = value; OnPropertyChanged(); }
        }

        // ── Commands (no-op for preview) ─────────────────────────────────────
        public ICommand KillSwitchCommand        { get; } = new RelayCommand(_ => { });
        public ICommand OpenSettingsCommand      { get; } = new RelayCommand(_ => { });
        public ICommand AddStrategyCommand       { get; } = new RelayCommand(_ => { });
        public ICommand RefreshStrategiesCommand { get; } = new RelayCommand(_ => { });
        public ICommand FetchOptionChainCommand  { get; } = new RelayCommand(_ => { });
        public ICommand ClearLogCommand          { get; } = new RelayCommand(_ => { });
        public ICommand OpenReportsCommand       { get; } = new RelayCommand(_ => { });
        public ICommand StartV2StrategyCommand   { get; } = new RelayCommand(_ => { });
        public ICommand StopV2StrategyCommand    { get; } = new RelayCommand(_ => { });
        public ICommand EditStrategyCommand      { get; } = new RelayCommand(_ => { });
        public ICommand ExitPositionCommand      { get; } = new RelayCommand(_ => { });

        // ── INotifyPropertyChanged ───────────────────────────────────────────
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
