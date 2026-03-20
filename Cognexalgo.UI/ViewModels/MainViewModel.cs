using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Cognexalgo.Core;
using Cognexalgo.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Linq; // For Close logic
using System.Windows; // For Window handling
using Cognexalgo.UI.Services; // [NEW]
using Cognexalgo.Core.Services; // [NEW]
using Cognexalgo.Core.Infrastructure.Services; // V2
using Cognexalgo.Core.Domain.Strategies; // HybridV2Strategy

namespace Cognexalgo.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly TradingEngine _engine;
        private readonly UiSettingsService _settingsService;

        // ─── V2 Integration ──────────────────────────────────────
        private V2Bridge? _v2;
        [ObservableProperty] private EnterpriseDashboardViewModel _enterpriseDashboard;

        // ─── Strategy Scheduler ───────────────────────────────────
        private readonly Cognexalgo.Core.Application.Services.StrategyScheduler _scheduler = new();

        // Window Bindings
        [ObservableProperty] private double _windowHeight;
        [ObservableProperty] private double _windowWidth;
        [ObservableProperty] private double _logPanelHeight;

        [ObservableProperty]
        private string _status = "Disconnected";
        
        [ObservableProperty]
        private double _ltpNifty;
        
        [ObservableProperty]
        private double _ltpBankNifty;

        [ObservableProperty]
        private double _ltpFinnifty = 23520.45;

        [ObservableProperty]
        private double _ltpMidcpNifty = 12150.10;

        [ObservableProperty]
        private double _ltpSensex = 79500.25;
        
        [ObservableProperty]
        private string _selectedOptionIndex = "NIFTY";

        [ObservableProperty]
        private double _totalMtm;

        [ObservableProperty]
        private int _runningStrategiesCount;

        [ObservableProperty]
        private int _pendingOrdersCount;

        [ObservableProperty]
        private int _exitedStrategiesCount;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TradingModeText))]
        [NotifyPropertyChangedFor(nameof(TradingModeColor))]
        private bool _isLiveMode = false;

        [ObservableProperty]
        private double _maxLoss = -50000;

        [ObservableProperty]
        private double _maxProfit = 50000;

        private readonly Cognexalgo.Core.Services.ReportExporter _exporter = new Cognexalgo.Core.Services.ReportExporter();
        private readonly GreeksService _greeksService = new();

        // [Phase 4] Analytics Properties
        [ObservableProperty] private double _profitEfficiency;
        [ObservableProperty] private double _rescuedCapital;
        [ObservableProperty] private double _avgSlippage;

        public ObservableCollection<Order> ClosedTradeAnalytics { get; } = new ObservableCollection<Order>();

        public string TradingModeText => IsLiveMode ? "LIVE MODE" : "PAPER MODE";
        public string TradingModeColor => IsLiveMode ? "#2ECC71" : "#E74C3C";

        // F9: Combined portfolio P&L across all running V2 strategies
        [ObservableProperty]
        private decimal _totalV2Pnl;

        // Design2: spot price shown in Option Chain header
        [ObservableProperty]
        private double _spotPrice;

        // Design2: intraday P&L sparkline
        public ObservableCollection<PnlPoint> PnlHistory { get; } = new();
        private DateTime _lastPnlSample = DateTime.MinValue;

        public ObservableCollection<HybridStrategyConfig> Strategies { get; } = new ObservableCollection<HybridStrategyConfig>();
        public ObservableCollection<Order> Orders { get; } = new ObservableCollection<Order>();
        public ObservableCollection<Position> Positions { get; } = new ObservableCollection<Position>(); // Changed from Order
        public ObservableCollection<Signal> Signals { get; } = new ObservableCollection<Signal>();
        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();
        public ObservableCollection<OptionChainItem> OptionChain { get; } = new ObservableCollection<OptionChainItem>();

        public AccountManagerViewModel AccountManager { get; } 
        public SafeExitService SafeExitService { get; private set; } // [NEW]

        private BootstrapperService _bootstrapper; 

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartEngineCommand))]
        private bool _isInitialized = false;

        /// <summary>True after StartEngine connects SmartStream. Drives the Connect button color.</summary>
        [ObservableProperty]
        private bool _isEngineRunning = false;

        [ObservableProperty]
        private double _loadingProgress = 0;

        [ObservableProperty]
        private string _loadingStatus = "Initializing...";

        private bool _isFetchingOptionChain = false;
        private bool _isFetchingPositions = false;
        private bool _isFetchingOrders = false;

        // COGNEX-CHANGE Part 1/2: tracks all tokens subscribed for the active chain
        // so they can be cleared and replaced when SelectedOptionIndex switches.
        private readonly HashSet<string> _subscribedChainTokens = new();

        public MainViewModel(TradingEngine engine)
        {
            // Initialize Engine
            _engine = engine;
            _engine.IsPaperTrading = !IsLiveMode; // Sync initial state
            
            // Initialize Services
            SafeExitService = new SafeExitService(_engine, _engine.MetadataContext); // [NEW]

            // Initialize Sub-ViewModels
            AccountManager = new AccountManagerViewModel(_engine);
            
            // Subscribe to Engine Events
            _engine.Ticker.OnTickReceived += OnTick;
            _engine.Ticker.OnStatusChanged += OnStatus;
            _engine.OnSignalReceived += OnSignal;
            
            // Initialize Auto-Refresh Timer
            _autoRefreshTimer = new System.Windows.Threading.DispatcherTimer();
            _autoRefreshTimer.Interval = TimeSpan.FromSeconds(3);
            _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;

            // COGNEX-CHANGE Part 3: Greeks recalc timer — 30s, zero API calls
            _greeksTimer = new System.Windows.Threading.DispatcherTimer();
            _greeksTimer.Interval = TimeSpan.FromSeconds(30);
            _greeksTimer.Tick += GreeksTimer_Tick;

            // Subscribe to Engine Events
            if (_engine.Logger != null)
            {
                _engine.Logger.OnLog += Logger_OnLog;
            }

            // Load Settings
            _settingsService = new UiSettingsService();
            var settings = _settingsService.Load();
            
            WindowHeight = settings.WindowHeight;
            WindowWidth = settings.WindowWidth;
            LogPanelHeight = settings.LogPanelHeight;
            MaxLoss = settings.MaxLoss;
            MaxProfit = settings.MaxProfit;

            // Start Bootstrapper Sequence
            _ = InitializeSystemAsync();
        }

        private async Task InitializeSystemAsync()
        {
            try 
            {
                IsInitialized = false;
                Status = "Bootstrapping...";
                LoadingProgress = 5;

                // Instantiate Bootstrapper (Temporary logic until DI is fully refactored)
                // Assuming Engine exposes Config and DataService is accessible
                // We might need to ensure DataService is initialized or pass dependencies.
                // For now, relying on Engine having what is needed.
                var config = (Microsoft.Extensions.Configuration.IConfiguration)System.Windows.Application.Current.Resources["Configuration"];
                _bootstrapper = new BootstrapperService(_engine, _engine.DataService, config); // Pass Config if available
                
                _bootstrapper.OnProgressChanged += (msg, percent) =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        LoadingStatus = msg;
                        LoadingProgress = percent;
                        Log($"[Bootstrapper] {msg}");
                    });
                };

                _bootstrapper.OnCriticalWarning += (msg) =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        System.Windows.MessageBox.Show(
                            msg,
                            "⚠ Clock Sync Warning",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Warning);
                    });
                };

                await _bootstrapper.InitializeAsync();

                // ─── V2 Bridge Init ──────────────────────────
                try
                {
                    LoadingStatus = "Initializing V2 services...";
                    LoadingProgress = 85;
                    _v2 = await V2Bridge.InitializeAsync(config);
                    EnterpriseDashboard = new EnterpriseDashboardViewModel(_v2);
                    _v2.Orchestrator.OnLog += (level, msg) =>
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            Log($"[V2-{level}] {msg}"));
                    };

                    // Reflect Orchestrator status changes onto the Strategies list in real time
                    _v2.Orchestrator.OnStatusChanged += (id, status) =>
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            var s = Strategies.FirstOrDefault(x => x.V2Id == id);
                            if (s != null) s.V2Status = status.ToString();
                        });
                    };

                    _engine.V2 = _v2;
                    Log("V2 Bridge initialized successfully.");
                }
                catch (Exception v2ex)
                {
                    Log($"V2 Bridge init warning (non-fatal): {v2ex.Message}", "WARN");
                }
                
                IsInitialized = true;
                Status = "Ready";
                LoadingStatus = "System Ready.";
                
                // Load UI Data after Bootstrap
                await LoadStrategies();
                await AccountManager.LoadAccountsAsync(); // Sequential load to avoid DbContext concurrency
                Log("Initialization Complete. All systems Ready. Click ⚡ Connect to start the live feed.");
            }
            catch (Exception ex)
            {
                Status = "Initialization Failed";
                LoadingStatus = $"Error: {ex.Message}";
                Log($"CRITICAL: Bootstrapper Failed - {ex.Message}", "ERROR");
                MessageBox.Show($"System Initialization Failed:\n{ex.Message}\n\nApp will operate in restricted mode.", "Bootstrapper Error", MessageBoxButton.OK, MessageBoxImage.Error);
                // Allow usage but maybe warn? Or keep IsInitialized false to block Start Engine.
                // IsInitialized = true; // Uncomment to allow bypass if desired
            }
        }

        private void Logger_OnLog(string level, string component, string message)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => 
            {
                string msg = $"{DateTime.Now:HH:mm:ss} [{level}] [{component}] {message}";
                Logs.Add(msg);
                if (Logs.Count > 200) Logs.RemoveAt(0);
            });
        }

        private System.Windows.Threading.DispatcherTimer _autoRefreshTimer;
        // COGNEX-CHANGE Part 3: Greeks recalc timer (no API calls, 30s interval)
        private System.Windows.Threading.DispatcherTimer _greeksTimer;

        private void AutoRefreshTimer_Tick(object sender, EventArgs e)
        {
             if (_engine.IsRunning)
             {
                 _ = FetchPositions();
                 _ = FetchOrders();
                 // _ = FetchOptionChain(SelectedOptionIndex); // Temporarily removed
             }
        }

        // COGNEX-CHANGE Part 3: recalculate IV + Greeks every 30s using cached LTPs, no API calls
        private void GreeksTimer_Tick(object sender, EventArgs e)
        {
            if (OptionChain.Count == 0) return;

            double spot = SelectedOptionIndex switch
            {
                "BANKNIFTY"  => LtpBankNifty,
                "FINNIFTY"   => LtpFinnifty,
                "MIDCPNIFTY" => LtpMidcpNifty,
                _            => LtpNifty
            };
            if (spot <= 0) return; // spot not yet ticked — skip this cycle

            const double r = 0.067;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var item in OptionChain)
                {
                    if (item.LTP <= 0) continue; // token not yet ticked — skip

                    double dte    = Math.Max(0.5, item.DaysToExpiry);
                    bool   isCall = item.IsCall;

                    double iv = _greeksService.CalculateIV(item.LTP, spot, item.Strike, dte, r, isCall);
                    if (iv < 0.01) iv = 0.15;

                    var g      = _greeksService.CalculateGreeks(spot, item.Strike, dte, r, iv, isCall);
                    item.IV    = Math.Round(iv * 100, 2);
                    item.Delta = g.Delta;
                    item.Theta = g.Theta;
                    item.Vega  = g.Vega;
                    item.Gamma = g.Gamma;
                }
            });
        }

        private async Task LoadStrategies()
        {
            try 
            {
                var strategies = await _engine.StrategyRepository.GetAllHybridStrategiesAsync(); 
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    Strategies.Clear();
                    foreach (var s in strategies) 
                    {
                        if (s == null) continue;
                        
                        // Initialize Runtime Properties
                        s.Status = s.IsActive ? "RUNNING" : "EXITED";
                        
                        // [FIX] Null safe check for EntryTime
                        s.EntryTime = (s.Legs != null && s.Legs.Any()) 
                            ? (s.Legs.FirstOrDefault()?.EntryTime ?? DateTime.Now) 
                            : DateTime.Now;
                            
                        s.Pnl = 0;
                        // Risk 5: Log TradingMode so we can verify LIVE strategies survive restart
                        Log($"[Load] Strategy '{s.Name}': Mode={s.TradingModeDisplay}, Active={s.IsActive}");
                        Strategies.Add(s);
                    }
                    UpdateCounts();
                });
            }
            catch (Exception ex)
            {
                Log($"Error Loading Strategies: {ex.Message}", "ERROR");
                Console.WriteLine($"[LoadStrategies] CRASH: {ex}");
            }
        }

        private void UpdateCounts()
        {
             RunningStrategiesCount = Strategies.Count(s => s.IsActive);
             ExitedStrategiesCount = Strategies.Count(s => !s.IsActive);
        }



        private void OnStatus(string status)
        {
            Status = status;
        }

        private void OnTick(TickerData data)
        {
            // Update Strategy PNL & LTP (Simulated/Calculated)
            System.Windows.Application.Current.Dispatcher.Invoke(() => 
            {
                if (data.Nifty != null) LtpNifty = data.Nifty.Ltp;
                if (data.BankNifty != null) LtpBankNifty = data.BankNifty.Ltp;
                if (data.FinNifty != null) LtpFinnifty = data.FinNifty.Ltp;
                if (data.MidcpNifty != null) LtpMidcpNifty = data.MidcpNifty.Ltp;
                if (data.Sensex != null) LtpSensex = data.Sensex.Ltp;

                // Update Global PnL
                // FIX 5: in paper mode, sum all positions (open unrealized + closed realized)
                TotalMtm = _engine.IsPaperTrading && Positions.Count > 0
                    ? Positions.Sum(p => p.Pnl)
                    : _engine.GetTotalPnL();

                // Update Strategy LTPs from global ticker
                foreach (var strategy in Strategies)
                {
                    if (strategy.IsActive)
                    {
                        // TODO: Map strategy to specific underline (NIFTY/BANKNIFTY)
                        strategy.Ltp = (decimal)LtpNifty; 
                    }
                }

                // ── Live unrealized P&L for paper positions ──────────────────────
                if (_engine.IsPaperTrading && Positions.Count > 0)
                {
                    foreach (var pos in Positions)
                    {
                        if (!double.TryParse(pos.NetQty, out double netQty))
                            continue;

                        // Closed position — P&L is set by FetchPositions; do not overwrite.
                        // Recalculate from avg prices only if Pnl is unexpectedly zero.
                        if (netQty == 0)
                        {
                            if (pos.Pnl == 0 && pos.BuyAvgPrice > 0 && pos.SellAvgPrice > 0)
                                pos.Pnl = pos.SellAvgPrice - pos.BuyAvgPrice;
                            continue;
                        }

                        double ltp;
                        if (pos.ParsedStrike > 0)
                        {
                            // Fast path: tick carries this option's LTP directly
                            if (!string.IsNullOrEmpty(pos.SymbolToken) &&
                                data.Options.TryGetValue(pos.SymbolToken, out var optInfo) && optInfo.Ltp > 0)
                                ltp = optInfo.Ltp;
                            else
                            {
                                ltp = GetOptionLtpFromStrategy(pos.TradingSymbol);
                                if (ltp <= 0)
                                {
                                    // Fallback: show entry price so PnL column is not blank
                                    ltp = pos.BuyAvgPrice > 0 ? pos.BuyAvgPrice : pos.SellAvgPrice;
                                    if (ltp <= 0) continue;
                                }
                            }
                        }
                        else
                        {
                            // Underlying / futures position: use spot price
                            ltp = pos.TradingSymbol switch
                            {
                                var s when s != null && s.Contains("BANKNIFTY")  => LtpBankNifty,
                                var s when s != null && s.Contains("FINNIFTY")   => LtpFinnifty,
                                var s when s != null && s.Contains("MIDCPNIFTY") => LtpMidcpNifty,
                                var s when s != null && s.Contains("SENSEX")     => LtpSensex,
                                var s when s != null && s.Contains("NIFTY")      => LtpNifty,
                                _                                                 => 0
                            };
                            if (ltp <= 0) continue;
                        }

                        pos.Ltp = ltp;
                        pos.Pnl = netQty > 0
                            ? (ltp - pos.BuyAvgPrice)  * netQty
                            : (pos.SellAvgPrice - ltp) * Math.Abs(netQty);

                        // ── Greeks computation for option positions ──────
                        if (pos.ParsedStrike > 0)
                        {
                            double dte = Math.Max(0.01, (pos.ParsedExpiry - DateTime.Now).TotalDays);
                            double iv = _greeksService.CalculateIV(
                                pos.BuyAvgPrice, ltp, pos.ParsedStrike, dte, 0.07, pos.ParsedIsCall);
                            if (iv < 0.01) iv = 0.15; // floor at 15% if bisection can't converge
                            var g = _greeksService.CalculateGreeks(
                                ltp, pos.ParsedStrike, dte, 0.07, iv, pos.ParsedIsCall);
                            pos.IV    = Math.Round(iv * 100, 2);
                            pos.Delta = Math.Round(g.Delta, 4);
                            pos.Gamma = Math.Round(g.Gamma, 6);
                            pos.Theta = Math.Round(g.Theta, 2);
                            pos.Vega  = Math.Round(g.Vega, 2);
                        }
                    }
                }

                // FIX 3: Keep CachedNiftyChain LTPs fresh from SmartStream ticks
                // so DynamicStrategy.ResolveATMOption always returns current prices.
                if (data.Options?.Count > 0)
                {
                    var chains = new[]
                    {
                        _v2?.CachedNiftyChain,
                        _v2?.CachedBankNiftyChain,
                        _v2?.CachedFinniftyChain,
                        _v2?.CachedMidcpniftyChain,
                        _v2?.CachedSensexChain
                    };
                    foreach (var chain in chains)
                    {
                        if (chain == null) continue;
                        foreach (var item in chain)
                        {
                            if (!string.IsNullOrEmpty(item.Token) &&
                                data.Options.TryGetValue(item.Token, out var optInfo) &&
                                optInfo.Ltp > 0)
                            {
                                item.LTP = optInfo.Ltp;
                            }
                        }
                    }

                    // COGNEX-CHANGE: Wire live ticks to UI OptionChain collection.
                    // OptionChainItem now implements INotifyPropertyChanged so the
                    // LTP setter raises PropertyChanged — WPF binding refreshes automatically.
                    // No extra Dispatcher.Invoke needed; we are already on the UI thread.
                    if (OptionChain?.Count > 0)
                    {
                        foreach (var item in OptionChain)
                        {
                            if (!string.IsNullOrEmpty(item.Token) &&
                                data.Options.TryGetValue(item.Token, out var optionTick) &&
                                optionTick.Ltp > 0)
                            {
                                item.LTP = optionTick.Ltp;
                            }
                        }
                    }
                }

                // ─── Intraday P&L sparkline sample (every 30 s) ──────────
                if ((DateTime.Now - _lastPnlSample).TotalSeconds >= 30)
                {
                    _lastPnlSample = DateTime.Now;
                    PnlHistory.Add(new PnlPoint { Time = DateTime.Now, Value = TotalMtm });
                    if (PnlHistory.Count > 200) PnlHistory.RemoveAt(0);
                }

                // ─── V2: Forward tick to V2 Orchestrator ─────
                if (_v2?.IsInitialized == true)
                {
                    _ = _v2.DispatchTickAsync(LtpNifty, LtpBankNifty, LtpFinnifty, LtpMidcpNifty, LtpSensex);
                    // F9: Update combined portfolio P&L
                    TotalV2Pnl = _v2.Orchestrator.TotalDailyPnl;

                    // ── Scheduler: auto-start strategies whose entry time has arrived ──
                    var readyConfigs = _scheduler.GetReadyStrategies(DateTime.Now);
                    foreach (var cfg in readyConfigs)
                        _ = StartV2Strategy(cfg);
                }
            });
        }

        private void OnSignal(Signal signal)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                Signals.Insert(0, signal); // Add to top
                if (Signals.Count > 100) Signals.RemoveAt(Signals.Count - 1);
            });
        }

        /// <summary>
        /// P1: Returns the live option premium for a given trading symbol.
        /// Priority: (1) SmartStream tick if subscribed, (2) V2 strategy leg Ltp.
        /// Returns 0 if neither source has a value yet.
        /// </summary>
        private double GetOptionLtpFromStrategy(string? tradingSymbol)
        {
            if (string.IsNullOrEmpty(tradingSymbol)) return 0;

            // 1. SmartStream: best source — token subscribed during FetchPositions
            var token = _engine?.TokenService?.GetToken(tradingSymbol);
            if (!string.IsNullOrEmpty(token))
            {
                double streamLtp = _engine!.SmartStream?.GetLastLtp(token) ?? 0;
                if (streamLtp > 0) return streamLtp;
            }

            // 2. V2 strategy leg: fallback when SmartStream hasn't received a tick yet
            if (_v2?.Orchestrator?.Contexts != null)
            {
                foreach (var ctx in _v2.Orchestrator.Contexts.Values)
                {
                    if (ctx.Strategy is HybridV2Strategy hvs)
                    {
                        var leg = hvs.GetConfig().Legs
                            .FirstOrDefault(l => l.TradingSymbol == tradingSymbol && l.Status == "OPEN");
                        if (leg != null && leg.Ltp > 0)
                            return leg.Ltp;
                    }
                }
            }

            return 0;
        }

        [RelayCommand(CanExecute = nameof(IsInitialized))]
        public async Task StartEngine()
        {
            // LIVE & PAPER: REQUIRE LOGIN (For Real Data)
            Status = "Connecting...";
            
            // Fetch Credentials from Database
            var creds = await _engine.CredentialsRepository.GetAsync();
            if (creds == null)
            {
                Log("ERROR: Missing Broker Credentials. Please configure them in Settings.", "ERROR");
                MessageBox.Show("Please configure Broker Credentials in Settings first.", "Missing Credentials", MessageBoxButton.OK, MessageBoxImage.Warning);
                Status = "Error";
                return;
            }
            Log("Credentials found. Connecting to Broker...");

            try 
            {
                await _engine.ConnectAsync(creds.ApiKey, creds.ClientCode, creds.Password, creds.TotpKey);
                
                // [NEW] Subscribe to Auth Events
                _engine.Api.OnSessionExpired += () => 
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => 
                    {
                        StopEngine();
                        MessageBox.Show("Session Expired! Please Re-login.", "Auth Failure", MessageBoxButton.OK, MessageBoxImage.Error);
                        Status = "Disconnected";
                    });
                };

                await _engine.Ticker.ConnectAsync();
                _engine.Start();
                Status = "Running";
                IsEngineRunning = true;

                // ── Share V1 auth session with V2 broker adapter ──
                _v2?.SyncBrokerAuth(_engine.Api);
                Log("V2 broker auth synced from V1 session.");

                // ── Connect order update WebSocket for real-time fill notifications ──
                if (_v2 != null && !string.IsNullOrEmpty(_engine.Api.JwtToken))
                {
                    _ = _v2.ConnectOrderUpdateWsAsync(_engine.Api.JwtToken);
                    Log("Order update WebSocket connecting...");
                }

                // Start Auto-Refresh
                _autoRefreshTimer.Start();
                // COGNEX-CHANGE Part 3: start Greeks timer together with auto-refresh
                _greeksTimer.Start();
                Log("Auto-Refresh Started (3s Interval, Greeks 30s Interval)");

                // Fix 7: Show Scrip Master age in System Log on every login
                string dateFile = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "scrip_master_date.txt");
                if (System.IO.File.Exists(dateFile))
                {
                    string savedDate = System.IO.File.ReadAllText(dateFile).Trim();
                    bool isToday = savedDate == DateTime.Today.ToString("yyyy-MM-dd");
                    Log($"[Scrip Master] Last downloaded: {savedDate} " +
                        $"{(isToday ? "✓ Fresh" : "⚠ STALE — will refresh on next chain load")}");
                }
                else
                {
                    Log("[Scrip Master] No cache date found — will download on next chain load.");
                }
            }
            catch (Exception ex)
            {
                Status = "Connection Failed";
                MessageBox.Show($"Login Failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void ToggleTradingMode()
        {
            IsLiveMode = !IsLiveMode;
            if (_engine != null)
            {
                _engine.IsPaperTrading = !IsLiveMode;
            }
            Log($"Switched to {(IsLiveMode ? "LIVE" : "PAPER")} Trading Mode");
        }

        public void Log(string message, string level = "INFO")
        {
            // Delegate completely to Engine Logger which will callback via event
            // "UI" is the component name
            _engine?.Logger?.Log("UI", message, level);
        }

        [RelayCommand]
        private void CreateStrategy()
        {
            var vm = new StrategyBuilderViewModel(_engine, () => 
            {
                 // On Save Success — legacy save done
                 LoadStrategies();
                 
                 // ─── V2: Sync to V2 DB ─────────────────────────
                 if (_v2?.IsInitialized == true && Strategies.Count > 0)
                 {
                     var latest = Strategies.LastOrDefault();
                     if (latest != null)
                     {
                         var adapter = new V2StrategyAdapter(_v2, _engine?.TokenService);
                         var latestSnapshot = latest; // capture for closure
                         _ = Task.Run(async () =>
                         {
                             var v2Id = await adapter.SyncToV2Async(latestSnapshot);
                             if (v2Id != null)
                             {
                                 System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                 {
                                     latestSnapshot.V2Id = v2Id;
                                     Log($"V2 Strategy synced: {v2Id}");
                                 });
                                 // Persist V2Id back to DB so it survives app restarts.
                                 // SaveHybridStrategyAsync does upsert-by-name and serialises
                                 // the full HybridStrategyConfig (including V2Id) to ConfigJson.
                                 try
                                 {
                                     await _engine.StrategyRepository.SaveHybridStrategyAsync(latestSnapshot);
                                 }
                                 catch (Exception saveEx)
                                 {
                                     System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                         Log($"V2Id save-back failed (non-fatal): {saveEx.Message}", "WARN"));
                                 }
                             }
                         });
                     }
                 }

                 Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w is Views.StrategyBuilderWindow)?.Close();
            });

            var window = new Views.StrategyBuilderWindow
            {
                DataContext = vm,
                Owner = Application.Current.MainWindow
            };
            window.ShowDialog();
        }

        [RelayCommand]
        public async Task DeleteStrategy(object parameter)
        {
            if (parameter is not HybridStrategyConfig strategy) return;
            
            // Assuming DeleteHybridStrategyAsync exists or using generic delete if ID is unique across tables
            // But since we are using HybridStrategyConfig, we should use the appropriate repo method.
            // If DeleteHybridStrategyAsync doesn't exist, we might need to add it or use DeleteAsync if it takes an ID and knows the type?
            // StrategyRepository.DeleteAsync takes an ID. 
            // If Hybrid strategies are in a different table, we need a specific delete method or Repo should handle it.
            // For now, I'll assume we need to add DeleteHybridStrategyAsync or use DeleteAsync if it works.
            // Converting to HybridStrategyEntity logic in Repo.
            // Let's use the new method I'll ensure exists or exist: DeleteHybridStrategyAsync.
            // Previously I tried to call DeleteAsync(strategy.Id).
            
            var result = MessageBox.Show(
                $"Delete strategy '{strategy.Name}'?\nThis cannot be undone.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            await _engine.StrategyRepository.DeleteHybridStrategyAsync(strategy.Id);
            await LoadStrategies();
            Log($"Deleted Strategy: {strategy.Name}");
        }

        /// <summary>
        /// Stop a V2 strategy that is running in the Orchestrator.
        /// Only active when the strategy has been synced (V2Id is populated).
        /// </summary>
        [RelayCommand]
        private void StopV2Strategy(object parameter)
        {
            if (parameter is not HybridStrategyConfig strategy) return;
            if (string.IsNullOrEmpty(strategy.V2Id))
            {
                Log($"Strategy '{strategy.Name}' has not been synced to V2 yet.", "WARN");
                return;
            }
            if (_v2?.IsInitialized != true)
            {
                Log("V2 Bridge is not initialized.", "WARN");
                return;
            }
            _v2.Orchestrator.StopStrategy(strategy.V2Id);
            strategy.IsDeployed = false;
            Log($"■ Stop requested for V2 strategy: {strategy.Name} ({strategy.V2Id})");
        }

        /// <summary>Toggle a strategy between Paper and Live mode (only when not running).</summary>
        [RelayCommand]
        private void ToggleStrategyMode(object parameter)
        {
            if (parameter is not HybridStrategyConfig strategy) return;
            if (strategy.V2Status == "Active")
            {
                Log($"Cannot change mode while '{strategy.Name}' is running. Stop it first.", "WARN");
                return;
            }

            if (!strategy.IsLiveMode)
            {
                // Switching TO live — confirm
                var result = System.Windows.MessageBox.Show(
                    $"Switch '{strategy.Name}' to LIVE MODE?\n\n" +
                    "Real orders will be placed on Angel One.\n" +
                    "Ensure your broker session is authenticated.",
                    "Confirm LIVE Trading",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);

                if (result != System.Windows.MessageBoxResult.Yes) return;
            }

            strategy.IsLiveMode = !strategy.IsLiveMode;
            Log($"Strategy '{strategy.Name}' mode: {strategy.TradingModeDisplay}");
        }

        /// <summary>
        /// Start (or re-start) a strategy in the V2 Orchestrator.
        /// If the strategy has no V2Id yet, it is synced first (idempotent).
        /// </summary>
        [RelayCommand]
        private async Task StartV2Strategy(object parameter)
        {
            if (parameter is not HybridStrategyConfig strategy) return;
            if (strategy.IsDeployed) return; // Guard against double-clicks and scheduler re-triggers
            if (_v2?.IsInitialized != true)
            {
                Log("V2 Bridge is not initialized.", "WARN");
                return;
            }

            // ── Live mode confirmation before start ──────────────────────────
            if (strategy.IsLiveMode)
            {
                if (_v2.BrokerAdapter == null || !_v2.BrokerAdapter.IsAuthenticated)
                {
                    Log($"Cannot start '{strategy.Name}' in LIVE mode — broker not authenticated.", "ERROR");
                    return;
                }

                var result = System.Windows.MessageBox.Show(
                    $"Start '{strategy.Name}' in LIVE MODE?\n\n" +
                    "Real orders will be placed on Angel One.\n" +
                    "This action cannot be undone once orders execute.",
                    "Confirm LIVE Start",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);

                if (result != System.Windows.MessageBoxResult.Yes) return;
            }

            // ── Ensure synced to V2 DB (non-fatal — strategy can run offline) ──
            if (string.IsNullOrEmpty(strategy.V2Id))
            {
                var adapter = new V2StrategyAdapter(_v2, _engine?.TokenService);
                string? v2Id = null;
                try { v2Id = await Task.Run(() => adapter.SyncToV2Async(strategy)); }
                catch (Exception ex) { Log($"V2 sync failed (non-fatal): {ex.Message}", "WARN"); }

                if (!string.IsNullOrEmpty(v2Id))
                {
                    strategy.V2Id = v2Id;
                    try { await _engine.StrategyRepository.SaveHybridStrategyAsync(strategy); }
                    catch (Exception ex) { Log($"V2Id persist failed (non-fatal): {ex.Message}", "WARN"); }
                    Log($"V2 Strategy synced: {v2Id}");
                }
                else
                {
                    // DB unavailable (e.g. Supabase IP blocked) — assign local ID and continue
                    strategy.V2Id = $"LOCAL-{DateTime.Now:yyyyMMdd}-{strategy.Id:D4}";
                    Log($"DB unavailable — strategy will run with local ID: {strategy.V2Id}", "WARN");
                }
            }

            // ── Reset leg statuses for a fresh run (prevents stale OPEN/EXITED from prior session) ──
            foreach (var leg in strategy.Legs)
            {
                leg.Status        = "PENDING";
                leg.CurrentReEntry = 0;
                leg.EntryPrice    = 0;
                leg.ExitPrice     = 0;
                leg.ExitReason    = "";
                leg.EntryTime     = null;
                leg.ExitTime      = null;
                leg.CalculatedStrike = 0;
                leg.SymbolToken   = null;
                leg.Ltp           = 0;
            }

            // ── Register RMS rules from strategy config ──────────────────────
            var v2Strategy = new Cognexalgo.Core.Domain.Strategies.HybridV2Strategy(strategy);
            var rmsConfig = new Cognexalgo.Core.Domain.ValueObjects.RmsConfig();
            if (strategy.MaxLossPercent > 0)
                rmsConfig.MaxLoss = strategy.MaxLossPercent * 100;
            if (strategy.MaxProfitPercent > 0)
                rmsConfig.MaxProfit = strategy.MaxProfitPercent * 100;
            if (strategy.Legs.Count > 0)
                rmsConfig.MaxReEntries = strategy.Legs.Max(l => l.MaxReEntry);

            // F5: MTM trailing SL + lock profit
            if (strategy.StrategyTrailingSL > 0)
            {
                rmsConfig.TrailingSL = strategy.StrategyTrailingSL;
                rmsConfig.TrailingIsPercent = strategy.StrategyTrailingIsPercent;
            }
            if (strategy.StrategyLockProfitAt > 0)
            {
                rmsConfig.LockProfitAt = strategy.StrategyLockProfitAt;
                rmsConfig.LockProfitTo = strategy.StrategyLockProfitTo;
            }

            _v2.StrategyRms.RegisterStrategy(v2Strategy.StrategyId, rmsConfig);

            // F2: Wire history cache for indicator-based entry conditions
            if (_v2.HistoryCache != null)
                v2Strategy.SetHistoryCacheService(_v2.HistoryCache);

            // ── Start in Orchestrator ────────────────────────────────────────
            strategy.IsDeployed = true; // Set BEFORE await to block re-entrant calls during async start
            try
            {
                await _v2.Orchestrator.StartStrategyAsync(v2Strategy);
                Log($"▶ Started V2 strategy: {strategy.Name} ({strategy.V2Id}) [{strategy.TradingModeDisplay}]");
            }
            catch (Exception startEx)
            {
                strategy.IsDeployed = false; // Reset on failure so user can retry
                Log($"Failed to start strategy '{strategy.Name}': {startEx.Message}", "ERROR");
            }
        }

        [RelayCommand]
        public void EditStrategy(object parameter)
        {
             if (parameter is not HybridStrategyConfig strategy) return;

             var vm = new StrategyBuilderViewModel(_engine, () => 
             {
                  // On Save Success
                  LoadStrategies();
                  Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w is Views.StrategyBuilderWindow)?.Close();
             });
             
             vm.LoadStrategy(strategy); // Load existing data

             var window = new Views.StrategyBuilderWindow
             {
                 DataContext = vm,
                 Owner = Application.Current.MainWindow
             };
             window.ShowDialog();
        }

        // Help: Open User Guide HTML in default browser
        [RelayCommand]
        public void OpenUserGuide()
        {
            // Search: next to exe, then walk up to solution root (up to 6 levels)
            string fileName = "COGNEX_UserGuide.html";
            string? found = null;
            var dir = new System.IO.DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = System.IO.Path.Combine(dir.FullName, fileName);
                if (System.IO.File.Exists(candidate)) { found = candidate; break; }
            }
            if (found == null) { System.Windows.MessageBox.Show("User guide not found.", "Help"); return; }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(found) { UseShellExecute = true });
        }

        // Open Strategy Scheduler window
        [RelayCommand]
        public void OpenScheduler()
        {
            var vm  = new SchedulerViewModel(_scheduler);
            var win = new Views.SchedulerWindow(vm) { Owner = Application.Current.MainWindow };
            win.Show();
        }

        // Calendar Trades popup
        [RelayCommand]
        public void OpenCalendarTrades(Cognexalgo.Core.Models.HybridStrategyConfig config)
        {
            if (_engine == null) return;
            var cal = _engine.GetCalendarStrategy(config.Id);
            if (cal == null)
            {
                System.Windows.MessageBox.Show("Calendar strategy is not running. Start the engine first.", "Not Running");
                return;
            }
            var vm  = new CalendarTradesViewModel(cal, config.Name);
            var win = new Views.CalendarTradesWindow(vm) { Owner = System.Windows.Application.Current.MainWindow };
            win.Show();
        }

        // Open Calendar Strategy configuration + control window
        [RelayCommand]
        public void OpenCalendarStrategy()
        {
            var vm  = new CalendarStrategyViewModel(_engine);
            var win = new Views.CalendarStrategyWindow { DataContext = vm };
            win.Owner = System.Windows.Application.Current.MainWindow;
            win.Show();
        }

        // Open Payoff Builder window
        [RelayCommand]
        public void OpenPayoffBuilder()
        {
            var vm = new PayoffBuilderViewModel(config =>
            {
                Strategies.Add(config);
                System.Windows.MessageBox.Show(
                    $"Strategy '{config.Name}' added.\nGo to the Strategies tab to deploy.",
                    "Added to Strategies",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            });
            // Pass spot price for the currently selected index
            vm.SelectedIndex = SelectedOptionIndex;
            vm.SpotPrice = SelectedOptionIndex switch
            {
                "BANKNIFTY"  => LtpBankNifty  > 0 ? LtpBankNifty  : 48000,
                "FINNIFTY"   => LtpFinnifty   > 0 ? LtpFinnifty   : 23500,
                "MIDCPNIFTY" => LtpMidcpNifty > 0 ? LtpMidcpNifty : 12000,
                "SENSEX"     => LtpSensex     > 0 ? LtpSensex     : 79000,
                _            => LtpNifty      > 0 ? LtpNifty      : 22000
            };

            // Seed option chain so premiums populate on first load
            vm.SetOptionChain(OptionChain);

            vm.LoadTemplateCommand.Execute(null);
            var win = new Views.DarkPayoffWindow(vm)
            {
                Owner = Application.Current.MainWindow
            };
            win.Show();
        }

        // F3: Open Performance Report window
        [RelayCommand]
        public void OpenReports()
        {
            if (_v2 == null) return;
            var vm = new ReportsViewModel(_v2);
            var win = new Views.ReportsWindow(vm) { Owner = Application.Current.MainWindow };
            _ = vm.LoadReportCommand.ExecuteAsync(null);
            win.Show();
        }

        [RelayCommand]
        public async Task StopEngine()
        {
            _engine.Stop();
            await _engine.Ticker.DisconnectAsync();
            
            _autoRefreshTimer.Stop();
            // COGNEX-CHANGE Part 3: stop Greeks timer together with auto-refresh
            _greeksTimer.Stop();
            Log("Auto-Refresh Stopped.");
            
            Status = "Stopped";
            IsEngineRunning = false;
            Log("Engine Stopped.");
        }
        [RelayCommand]
        public void OpenSettings()
        {
            var window = new Views.SettingsWindow();
            var vm = new SettingsViewModel(_engine, window);
            window.DataContext = vm;
            window.Owner = Application.Current.MainWindow;
            window.ShowDialog();
        }

        [RelayCommand]
        public async Task ExitAll()
        {
            Log("Global Exit Triggered. Squaring off all positions...");
            
            // V2: Kill Switch — stop all V2 strategies immediately
            _v2?.KillAll();
            
            await _engine.SquareOffAll();
            Log("Global Square-off completed.", "SUCCESS");
        }

        [RelayCommand]
        public void Reset()
        {
            Log("Resetting Dashboard...");
        }

        [RelayCommand]
        public void SaveRms()
        {

            Log($"RMS Saved: MaxLoss={MaxLoss}, MaxProfit={MaxProfit}");
            SaveSettings(); // Persist immediately
        }

        public void SaveSettings()
        {
            var settings = new UiSettings
            {
                WindowHeight = WindowHeight,
                WindowWidth = WindowWidth,
                LogPanelHeight = LogPanelHeight,
                MaxLoss = MaxLoss,
                MaxProfit = MaxProfit
            };
            _settingsService.Save(settings);
        }

        [RelayCommand]
        public void ExportAnalytics()
        {
            if (ClosedTradeAnalytics.Count == 0)
            {
                MessageBox.Show("No closed trade data available to export.", "Export Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv",
                FileName = $"TradeLog_{DateTime.Now:yyyyMMdd_HHmm}.csv",
                Title = "Export Trade Analytics"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    string csv = _exporter.ExportToCsv(ClosedTradeAnalytics);
                    _exporter.SaveToFile(csv, sfd.FileName);
                    Log($"✓ Report exported to: {sfd.FileName}", "SUCCESS");
                }
                catch (Exception ex)
                {
                    Log($"✗ Export failed: {ex.Message}", "ERROR");
                    MessageBox.Show($"Failed to export report: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        public ObservableCollection<Holding> Holdings { get; } = new ObservableCollection<Holding>();
        
        [RelayCommand]
        public async Task FetchOptionChain(string index = "NIFTY")
        {
            if (_engine == null || _engine.DataService == null || _isFetchingOptionChain) return;

            try
            {
                _isFetchingOptionChain = true;
                var idx = string.IsNullOrEmpty(index) ? SelectedOptionIndex : index;

                Log($"[OptionChain] === Starting for {idx} ===");
                Log($"[OptionChain] Engine running: {_engine?.IsRunning}");
                Log($"[OptionChain] JWT present: {!string.IsNullOrEmpty(_engine?.Api?.JwtToken)}");
                Log($"[OptionChain] DataService null: {_engine?.DataService == null}");

                // Auto-refresh Scrip Master if it is stale (new day / post-expiry)
                bool stale = await _engine.TokenService.IsScripMasterStale();
                if (stale)
                {
                    Log("⚠ Scrip Master is stale — refreshing before chain load...", "WARN");
                    try
                    {
                        await _engine.TokenService.LoadScripMasterSmartAsync();
                        int smCount = _engine.TokenService.GetSymbolCount();
                        Log($"✓ Scrip Master ready: {smCount:N0} symbols.");
                    }
                    catch (Exception smEx)
                    {
                        Log($"✗ Scrip Master refresh failed: {smEx.Message}", "ERROR");
                        Log("Cannot load option chain without Scrip Master. " +
                            "Click '🔄 Refresh Instruments' and check your internet connection.", "ERROR");
                        return;
                    }
                }

                var chain = await _engine.DataService.BuildOptionChainAsync(idx, "WEEKLY");

                Log($"[OptionChain] Raw chain count: {chain?.Count ?? 0}");

                if (chain == null || chain.Count == 0)
                {
                    Log($"[OptionChain] ⚠️ Empty chain — check [DataService] entries above for root cause", "WARN");
                    return;
                }

                // Use already-streaming live LTP for spot (zero API cost); fall back to REST call
                double spot = idx switch
                {
                    "BANKNIFTY"  => LtpBankNifty,
                    "FINNIFTY"   => LtpFinnifty,
                    "MIDCPNIFTY" => LtpMidcpNifty,
                    _            => LtpNifty
                };
                if (spot <= 0)
                    spot = await _engine.DataService.GetSpotPriceAsync(idx);

                var greeksSvc = new GreeksService();
                const double r = 0.067; // RBI repo rate ~6.7%

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    OptionChain.Clear();
                    foreach (var item in chain.OrderBy(i => i.Strike).ThenBy(i => i.OptionType))
                    {
                        // min 0.5 day so Black-Scholes denominator never reaches zero
                        double dte    = Math.Max(0.5, item.DaysToExpiry);
                        bool   isCall = item.IsCall;

                        // Solve for market-implied IV from the live LTP, then compute all Greeks
                        double iv = greeksSvc.CalculateIV(item.LTP, spot, item.Strike, dte, r, isCall);
                        if (iv < 0.01) iv = 0.15; // floor at 15% if bisection can't converge

                        var g      = greeksSvc.CalculateGreeks(spot, item.Strike, dte, r, iv, isCall);
                        item.IV    = Math.Round(iv * 100, 2); // store as percentage for display
                        item.Delta = g.Delta;
                        item.Theta = g.Theta;
                        item.Vega  = g.Vega;
                        item.Gamma = g.Gamma;

                        OptionChain.Add(item);
                    }
                });

                // COGNEX-CHANGE Part 2: drop stale subscriptions before subscribing new chain tokens
                _subscribedChainTokens.Clear();

                // COGNEX-CHANGE Part 1: subscribe every strike token so SmartStream ticks update LTP
                if (_engine.SmartStream?.IsConnected == true)
                {
                    var chainTokens = OptionChain
                        .Where(i => !string.IsNullOrEmpty(i.Token))
                        .Select(i => i.Token)
                        .Distinct()
                        .ToList();
                    if (chainTokens.Count > 0)
                    {
                        _ = _engine.SmartStream.SubscribeAsync(chainTokens, "NFO");
                        foreach (var t in chainTokens) _subscribedChainTokens.Add(t);
                        Log($"[OptionChain] Subscribed {chainTokens.Count} tokens to SmartStream.");
                    }
                }

                SpotPrice = spot;
                Log($"[OptionChain] Spot price used: {spot:N2}");
                Log($"[OptionChain] Final OptionChain count: {OptionChain.Count}");
                Log($"✓ Option chain: {OptionChain.Count} strikes with market IV & Greeks for {idx}.", "SUCCESS");

                // Cache option chain for V2 strategy strike resolution (all 5 indices)
                if (_v2 != null)
                {
                    var chainList = OptionChain.ToList();
                    switch (idx)
                    {
                        case "BANKNIFTY":  _v2.CachedBankNiftyChain    = chainList; break;
                        case "FINNIFTY":   _v2.CachedFinniftyChain     = chainList; break;
                        case "MIDCPNIFTY": _v2.CachedMidcpniftyChain   = chainList; break;
                        case "SENSEX":     _v2.CachedSensexChain        = chainList; break;
                        default:           _v2.CachedNiftyChain         = chainList; break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Option chain error: {ex.Message}", "ERROR");
            }
            finally
            {
                _isFetchingOptionChain = false;
            }
        }
        
        [RelayCommand]
        public async Task FetchHoldings()
        {
            if (_engine.Api == null) return;
            try 
            {
                var holdings = await _engine.Api.GetHoldingsAsync();
                System.Windows.Application.Current.Dispatcher.Invoke(() => 
                {
                    Holdings.Clear();
                    foreach(var h in holdings) Holdings.Add(h);
                });
                Log($"Fetched {holdings.Count} Holdings.");
            }
            catch(Exception ex)
            {
                Log($"Error Fetching Holdings: {ex.Message}");
            }
        }

        [RelayCommand]
        public async Task FetchPositions()
        {
            // Check if engine is initialized
            if (_engine == null || _isFetchingPositions) 
            {
                return;
            }
            
            try 
            {
                _isFetchingPositions = true;
                
                System.Windows.Application.Current.Dispatcher.Invoke(() => 
                {
                    Positions.Clear();
                });

                if (_engine.IsPaperTrading)
                {
                    var allOrders = await _engine.OrderRepository.GetAllAsync();
                    var orders = allOrders.Where(o => o.Timestamp.Date == DateTime.Today).ToList();
                    if (orders.Any())
                    {
                        var mockPositions = new System.Collections.Generic.List<Position>();
                        foreach (var g in orders.GroupBy(o => o.Symbol))
                        {
                            double buyQtyTotal  = g.Where(o => o.TransactionType == "BUY").Sum(o => o.Qty);
                            double sellQtyTotal = g.Where(o => o.TransactionType == "SELL").Sum(o => o.Qty);
                            double buyTotal     = g.Where(o => o.TransactionType == "BUY").Sum(o => o.Qty * o.Price);
                            double sellTotal    = g.Where(o => o.TransactionType == "SELL").Sum(o => o.Qty * o.Price);
                            double buyAvg       = buyQtyTotal  > 0 ? buyTotal  / buyQtyTotal  : 0;
                            double sellAvg      = sellQtyTotal > 0 ? sellTotal / sellQtyTotal : 0;
                            // FIX 3: direct sell-proceeds minus buy-cost — works for all strategies
                            double realizedPnl  = sellTotal - buyTotal;
                            double netQty       = buyQtyTotal - sellQtyTotal;

                            _engine.Logger?.Log("UI",
                                $"[Positions] {g.Key}: " +
                                $"BuyCost=₹{buyTotal:F2} SellValue=₹{sellTotal:F2} " +
                                $"NetQty={netQty} P&L=₹{realizedPnl:F2}");

                            mockPositions.Add(new Position
                            {
                                TradingSymbol = g.Key,
                                NetQty        = netQty.ToString(),
                                BuyAvgPrice   = buyAvg,
                                SellAvgPrice  = sellAvg,
                                AvgNetPrice   = buyQtyTotal > 0 ? buyAvg : sellAvg,
                                Pnl           = realizedPnl,
                                Status        = netQty != 0 ? "OPEN" : "CLOSED",
                                Exchange      = (g.Key.StartsWith("NIFTY") ||
                                                 g.Key.StartsWith("BANKNIFTY") ||
                                                 g.Key.StartsWith("FINNIFTY") ||
                                                 g.Key.StartsWith("MIDCPNIFTY") ||
                                                 g.Key.StartsWith("SENSEX"))
                                                 ? (g.Key.Length > 10 ? "NFO" : "NSE")
                                                 : "NSE",
                                ProductType   = "MIS"
                            });
                        }

                        // Parse option symbol for greeks computation
                        foreach (var p in mockPositions)
                        {
                            if (SymbolParser.TryParse(p.TradingSymbol, out _, out var exp, out var stk, out var call))
                            {
                                p.ParsedStrike = stk;
                                p.ParsedExpiry = exp;
                                p.ParsedIsCall = call;
                            }
                        }

                        // P2: Populate StopLoss / Target / EntryPrice from running strategy legs
                        if (_v2?.Orchestrator?.Contexts != null)
                        {
                            var legsBySymbol = _v2.Orchestrator.Contexts.Values
                                .Where(c => c.Strategy is HybridV2Strategy)
                                .SelectMany(c => ((HybridV2Strategy)c.Strategy).GetConfig().Legs)
                                .Where(l => !string.IsNullOrEmpty(l.TradingSymbol))
                                .GroupBy(l => l.TradingSymbol!)
                                .ToDictionary(g => g.Key, g => g.First());

                            foreach (var p in mockPositions)
                            {
                                if (p.TradingSymbol != null && legsBySymbol.TryGetValue(p.TradingSymbol, out var leg))
                                {
                                    p.StopLoss   = leg.StopLossPrice;
                                    p.Target     = leg.TargetPrice;
                                    p.EntryPrice = leg.EntryPrice;
                                }
                            }
                        }

                        // P1: Resolve option token from TokenService and subscribe to SmartStream
                        // so GetLastLtp(token) returns live option premiums on subsequent ticks.
                        var tokensToSubscribe = new System.Collections.Generic.List<string>();
                        foreach (var p in mockPositions.Where(p => p.ParsedStrike > 0))
                        {
                            // Priority: saved order token > option chain token > TokenService lookup
                            var savedToken = orders
                                .FirstOrDefault(o => o.Symbol == p.TradingSymbol && !string.IsNullOrEmpty(o.Token))
                                ?.Token;
                            var chainItem = _v2?.CachedNiftyChain?
                                .FirstOrDefault(c => c.Symbol == p.TradingSymbol);
                            var tok = savedToken
                                   ?? chainItem?.Token
                                   ?? _engine.TokenService?.GetToken(p.TradingSymbol);
                            if (!string.IsNullOrEmpty(tok))
                            {
                                p.SymbolToken = tok;
                                tokensToSubscribe.Add(tok);
                            }
                            else
                            {
                                _engine.Logger?.Log("UI",
                                    $"[FetchPositions] Token not found for {p.TradingSymbol} — " +
                                    $"live LTP will not update until option chain is refreshed.", "WARN");
                            }
                        }
                        if (tokensToSubscribe.Count > 0 && _engine.SmartStream?.IsConnected == true)
                            _ = _engine.SmartStream.SubscribeAsync(tokensToSubscribe, "NFO");

                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            foreach (var p in mockPositions) Positions.Add(p);
                        });
                        Log($"Fetched {mockPositions.Count} Paper Positions.");
                    }
                }
                else if (_engine.Api != null)
                {
                    var positions = await _engine.Api.GetPositionAsync();
                    if (positions == null)
                    {
                        Log("No positions data received from API.");
                        return;
                    }
                    
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var p in positions)
                        {
                            double.TryParse(p.NetQty, out double parsedQty);
                            p.Status = (parsedQty == 0) ? "CLOSED" : "OPEN";
                            if (SymbolParser.TryParse(p.TradingSymbol, out _, out var exp, out var stk, out var call))
                            {
                                p.ParsedStrike = stk;
                                p.ParsedExpiry = exp;
                                p.ParsedIsCall = call;
                            }
                            Positions.Add(p);
                        }
                    });
                    Log($"Fetched {positions.Count} Live Positions.");

                    // ── Enrich positions with trade book fill prices ──────────
                    // Best-effort: overwrite BuyAvgPrice / SellAvgPrice from actual fills
                    // which are more granular than the Angel One position API's avgnetprice.
                    try
                    {
                        var trades = await _engine.Api.GetTradeBookAsync();
                        if (trades?.Count > 0)
                        {
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                foreach (var pos in Positions)
                                {
                                    var fills = trades
                                        .Where(t => t.Symbol == pos.TradingSymbol &&
                                                    string.Equals(t.Status, "complete",
                                                        StringComparison.OrdinalIgnoreCase))
                                        .ToList();
                                    if (fills.Count == 0) continue;

                                    double buyFillQty  = fills.Where(f => f.TransactionType == "BUY").Sum(f => f.Qty);
                                    double sellFillQty = fills.Where(f => f.TransactionType == "SELL").Sum(f => f.Qty);
                                    if (buyFillQty  > 0)
                                        pos.BuyAvgPrice  = fills.Where(f => f.TransactionType == "BUY").Sum(f => f.Qty * f.Price)  / buyFillQty;
                                    if (sellFillQty > 0)
                                        pos.SellAvgPrice = fills.Where(f => f.TransactionType == "SELL").Sum(f => f.Qty * f.Price) / sellFillQty;
                                }
                            });
                            Log($"Trade book enrichment applied ({trades.Count} fills).");
                        }
                    }
                    catch { /* trade book enrichment is best-effort; never fail FetchPositions */ }
                }

                // ── V2 Orchestrator Positions ─────────────────────────────────────────
                // Show OPEN legs from running V2 strategies regardless of SQLite/API source.
                if (_v2?.IsInitialized == true && _v2.Orchestrator?.Contexts != null)
                {
                    var existingSymbols = new System.Collections.Generic.HashSet<string>(
                        Positions.Select(p => p.TradingSymbol ?? ""),
                        StringComparer.OrdinalIgnoreCase);

                    var v2Positions = new System.Collections.Generic.List<Position>();
                    foreach (var ctx in _v2.Orchestrator.Contexts.Values)
                    {
                        if (ctx.Strategy is not HybridV2Strategy hvs) continue;
                        var config = hvs.GetConfig();
                        foreach (var leg in config.Legs.Where(l => l.Status == "OPEN" &&
                                 !string.IsNullOrEmpty(l.TradingSymbol)))
                        {
                            if (existingSymbols.Contains(leg.TradingSymbol)) continue;
                            int lotSize = leg.LotSize > 0 ? leg.LotSize : 65;
                            int qty = leg.TotalLots * lotSize;
                            bool isBuy = leg.Action == ActionType.Buy;
                            double netQty = isBuy ? qty : -qty;

                            var pos = new Position
                            {
                                TradingSymbol = leg.TradingSymbol,
                                SymbolToken   = leg.SymbolToken ?? "",
                                Exchange      = "NFO",
                                NetQty        = netQty.ToString(),
                                BuyAvgPrice   = isBuy  ? leg.EntryPrice : 0,
                                SellAvgPrice  = !isBuy ? leg.EntryPrice : 0,
                                AvgNetPrice   = leg.EntryPrice,
                                EntryPrice    = leg.EntryPrice,
                                StopLoss      = leg.StopLossPrice,
                                Target        = leg.TargetPrice,
                                Status        = "OPEN"
                            };
                            if (SymbolParser.TryParse(leg.TradingSymbol, out _, out var exp, out var stk, out var call))
                            {
                                pos.ParsedStrike = stk;
                                pos.ParsedExpiry = exp;
                                pos.ParsedIsCall = call;
                            }
                            v2Positions.Add(pos);
                            existingSymbols.Add(leg.TradingSymbol);
                        }
                    }

                    // Subscribe live option tokens to SmartStream
                    var v2Tokens = v2Positions
                        .Where(p => !string.IsNullOrEmpty(p.SymbolToken) && p.ParsedStrike > 0)
                        .Select(p => p.SymbolToken!)
                        .Distinct().ToList();
                    if (v2Tokens.Count > 0 && _engine.SmartStream?.IsConnected == true)
                        _ = _engine.SmartStream.SubscribeAsync(v2Tokens, "NFO");

                    if (v2Positions.Count > 0)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            foreach (var p in v2Positions) Positions.Add(p);
                        });
                        Log($"Added {v2Positions.Count} V2 strategy positions.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error Fetching Positions: {ex.Message}");
            }
            finally
            {
                _isFetchingPositions = false;
            }
        }

        [RelayCommand]
        public async Task ClearPositions()
        {
            if (_engine?.OrderRepository == null) return;
            var result = System.Windows.MessageBox.Show(
                "This will permanently delete ALL paper trade orders. Continue?",
                "Clear Positions", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (result != System.Windows.MessageBoxResult.Yes) return;
            try
            {
                await _engine.OrderRepository.ClearAllAsync();
                System.Windows.Application.Current.Dispatcher.Invoke(() => Positions.Clear());
                Log("All paper positions cleared.");
            }
            catch (Exception ex)
            {
                Log($"Error clearing positions: {ex.Message}");
            }
        }

        [RelayCommand]
        public async Task FetchOrders()
        {
            if (_engine == null || _isFetchingOrders) return;

            try
            {
                _isFetchingOrders = true;
                
                var orders = new System.Collections.Generic.List<Order>();
                
                if (_engine.IsPaperTrading)
                {
                     var allOrders = await _engine.OrderRepository.GetAllAsync();
                     orders = allOrders.Where(o => o.Timestamp.Date == DateTime.Today).ToList();
                }
                else if (_engine.Api != null)
                {
                     var apiOrders = await _engine.Api.GetOrderBookAsync();
                     if (apiOrders != null) orders.AddRange(apiOrders);
                }

                if (orders == null || !orders.Any()) return;

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    // Basic sync: Clear and Add for now. 
                    // Optimization: update existing if needed, but OrderBook usually grows
                    // Reverse order to show newest first
                    Orders.Clear();
                    ClosedTradeAnalytics.Clear();

                    double totalProtected = 0;
                    double totalActual = 0;
                    double totalSlippage = 0;
                    int exitCount = 0;

                    foreach (var order in orders.OrderByDescending(o => o.Timestamp))
                    {
                        Orders.Add(order);

                        // Capture Analytics for Closed Trades
                        if (order.PotentialProfit > 0)
                        {
                            ClosedTradeAnalytics.Add(order);
                            
                            totalProtected += order.ProtectedProfit;
                            totalActual += order.ActualProfit;
                            totalSlippage += Math.Abs(order.ProtectedProfit - order.ActualProfit);
                            exitCount++;
                        }
                    }

                    // Update Aggregated Analytics
                    RescuedCapital = totalProtected;
                    ProfitEfficiency = totalProtected > 0 ? (totalActual / totalProtected) * 100 : 0;
                    AvgSlippage = exitCount > 0 ? totalSlippage / exitCount : 0;
                });
                // Log($"Fetched {orders.Count} Orders."); // Verbose logging, maybe skip
            }
            finally
            {
                _isFetchingOrders = false;
            }
        }

        // Design2 command aliases
        [RelayCommand] public void KillSwitch() => _ = ExitAll();
        [RelayCommand] public void ClearLog() => Logs.Clear();
        [RelayCommand] public void AddStrategy() => CreateStrategy();
        [RelayCommand] public async Task RefreshStrategies() => await LoadStrategies();

        /// <summary>
        /// Force-downloads a fresh Scrip Master from AngelOne and shows next expiry dates.
        /// Use this when the option chain is empty after weekly expiry or on a new day.
        /// </summary>
        [RelayCommand]
        public async Task RefreshScripMaster()
        {
            if (_engine?.TokenService == null) return;
            try
            {
                Log("⟳ Downloading fresh Scrip Master from AngelOne...");
                await _engine.TokenService.DownloadAndLoadScripMasterAsync();
                await _engine.TokenService.SaveScripMasterDate();

                int count = _engine.TokenService.GetSymbolCount();
                Log($"✓ Scrip Master loaded: {count:N0} symbols.", "SUCCESS");

                var niftyExpiry    = _engine.TokenService.GetNextExpiry("NIFTY");
                var bankExpiry     = _engine.TokenService.GetNextExpiry("BANKNIFTY");
                var finniftyExpiry = _engine.TokenService.GetNextExpiry("FINNIFTY");
                Log($"  NIFTY next expiry:     {niftyExpiry:dd-MMM-yyyy}");
                Log($"  BANKNIFTY next expiry: {bankExpiry:dd-MMM-yyyy}");
                Log($"  FINNIFTY next expiry:  {finniftyExpiry:dd-MMM-yyyy}");

                Log("Auto-loading NIFTY option chain...");
                await FetchOptionChain("NIFTY");
            }
            catch (Exception ex)
            {
                Log($"✗ Scrip Master download FAILED: {ex.Message}", "ERROR");
                System.Windows.MessageBox.Show(
                    $"Failed to download Scrip Master from AngelOne.\n\n" +
                    $"Error: {ex.Message}\n\n" +
                    $"Possible causes:\n" +
                    $"• Not connected to internet\n" +
                    $"• AngelOne API is down\n" +
                    $"• JWT token expired — try reconnecting\n" +
                    $"• Scrip Master URL has changed",
                    "Scrip Master Download Failed",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public async Task<bool> ExitPosition(Position position)
        {
            if (position == null) return false;

            Log($"[Exit] {position.TradingSymbol} " +
                $"NetQty={position.NetQty} " +
                $"Mode={(_engine.IsPaperTrading ? "PAPER" : "LIVE")}");

            try
            {
                // BUG 3 FIX — parse NetQty safely ("50.0" → double → abs int)
                if (!double.TryParse(position.NetQty,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double netQtyDouble) || netQtyDouble == 0)
                {
                    Log($"[Exit] Skipped — NetQty is zero or invalid: '{position.NetQty}'", "WARN");
                    return false;
                }

                int qty = (int)Math.Abs(netQtyDouble);
                string exitAction = netQtyDouble > 0 ? "SELL" : "BUY";

                // BUG 4 FIX — defaults for paper positions that have null Exchange/ProductType
                string exchange    = position.Exchange
                                     ?? (position.ParsedStrike > 0 ? "NFO" : "NSE");
                string productType = position.ProductType ?? "MIS";
                string token       = position.SymbolToken ?? "";

                Log($"[Exit] Placing {exitAction} {qty} {position.TradingSymbol} @ MARKET");

                // BUG 2 FIX — separate paper and live paths
                if (_engine.IsPaperTrading)
                {
                    // Paper exit — resolve fill price from live LTP or entry price
                    double exitPrice = position.Ltp > 0
                                       ? position.Ltp
                                       : (netQtyDouble > 0 ? position.BuyAvgPrice : position.SellAvgPrice);
                    if (exitPrice <= 0) exitPrice = position.AvgNetPrice;

                    var exitOrder = new Order
                    {
                        Symbol          = position.TradingSymbol,
                        Token           = token,
                        TransactionType = exitAction,
                        Qty             = qty,
                        Price           = exitPrice,
                        ProductType     = productType,
                        Status          = "complete",
                        Timestamp       = DateTime.Now,
                        StrategyName    = "Manual Exit"
                    };

                    await _engine.OrderRepository.AddAsync(exitOrder);

                    position.Status = "CLOSED";
                    position.NetQty = "0";

                    Log($"✓ [PAPER] Exit order saved: {exitAction} {qty} {position.TradingSymbol} @ ₹{exitPrice:F2}", "SUCCESS");

                    await Task.Delay(500);
                    await FetchPositions();
                    return true;
                }
                else
                {
                    // Live exit — call broker API
                    if (_engine.Api == null)
                    {
                        Log("[Exit] Not connected to broker API.", "ERROR");
                        return false;
                    }

                    string orderId = await _engine.Api.PlaceOrderAsync(
                        symbol:          position.TradingSymbol,
                        token:           token,
                        transactionType: exitAction,
                        qty:             qty,
                        price:           0,
                        variety:         "NORMAL",
                        productType:     productType,
                        exchange:        exchange);

                    if (!string.IsNullOrEmpty(orderId))
                    {
                        Log($"✓ Exit order placed: {orderId}", "SUCCESS");
                        position.Status = "EXITED";
                        await Task.Delay(1000);
                        await FetchPositions();
                        return true;
                    }
                    else
                    {
                        Log($"✗ Exit order failed for {position.TradingSymbol}", "ERROR");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[Exit] Error: {ex.Message}", "ERROR");
                return false;
            }
        }
    }

    public class PnlPoint
    {
        public DateTime Time  { get; set; }
        public double   Value { get; set; }
    }
}
