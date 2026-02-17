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

namespace Cognexalgo.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly TradingEngine _engine;
        private readonly UiSettingsService _settingsService;

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

        // [Phase 4] Analytics Properties
        [ObservableProperty] private double _profitEfficiency;
        [ObservableProperty] private double _rescuedCapital;
        [ObservableProperty] private double _avgSlippage;

        public ObservableCollection<Order> ClosedTradeAnalytics { get; } = new ObservableCollection<Order>();

        public string TradingModeText => IsLiveMode ? "LIVE MODE" : "PAPER MODE";
        public string TradingModeColor => IsLiveMode ? "#2ECC71" : "#E74C3C";

        public ObservableCollection<HybridStrategyConfig> Strategies { get; } = new ObservableCollection<HybridStrategyConfig>();
        public ObservableCollection<Order> Orders { get; } = new ObservableCollection<Order>();
        public ObservableCollection<Position> Positions { get; } = new ObservableCollection<Position>(); // Changed from Order
        public ObservableCollection<Signal> Signals { get; } = new ObservableCollection<Signal>();
        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();
        public ObservableCollection<OptionChainItem> OptionChain { get; } = new ObservableCollection<OptionChainItem>();

        public AccountManagerViewModel AccountManager { get; } 

        public MainViewModel(TradingEngine engine)
        {
            // Initialize Engine
            _engine = engine;
            _engine.IsPaperTrading = !IsLiveMode; // Sync initial state
            
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

            // Load Data
            _ = LoadStrategies();

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

            // Initialize Auto-Start for Debugging (Disabled for Production)
            // InitializeAutoStart();
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

        private void AutoRefreshTimer_Tick(object sender, EventArgs e)
        {
             if (_engine.IsRunning)
             {
                 _ = FetchPositions();
                 _ = FetchOrders();
                 _ = FetchOptionChain(SelectedOptionIndex);
             }
        }

        private async Task LoadStrategies()
        {
            var strategies = await _engine.StrategyRepository.GetAllHybridStrategiesAsync(); // [Updated] Fetch Hybrid Strategies
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                Strategies.Clear();
                foreach (var s in strategies) 
                {
                    // Initialize Runtime Properties
                    s.Status = s.IsActive ? "RUNNING" : "EXITED";
                    s.EntryTime = s.Id == 0 ? DateTime.Now : s.Legs.FirstOrDefault()?.EntryTime ?? DateTime.Now; // Mock/Approx
                    s.Pnl = 0; // Will be updated by OnTick
                    
                    Strategies.Add(s);
                }
                UpdateCounts();
            });
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
                TotalMtm = _engine.GetTotalPnL();

                // Update Strategy LTPs from global ticker
                foreach (var strategy in Strategies)
                {
                    if (strategy.IsActive)
                    {
                        // TODO: Map strategy to specific underline (NIFTY/BANKNIFTY)
                        strategy.Ltp = (decimal)LtpNifty; 
                    }
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

        [RelayCommand]
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
                
                // Start Auto-Refresh
                _autoRefreshTimer.Start();
                Log("Auto-Refresh Started (3s Interval)");
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
                 // On Save Success
                 LoadStrategies();
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
            
            await _engine.StrategyRepository.DeleteHybridStrategyAsync(strategy.Id);
            await LoadStrategies();
            Log($"Deleted Strategy: {strategy.Name}");
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

        [RelayCommand]
        public async Task StopEngine()
        {
            _engine.Stop();
            await _engine.Ticker.DisconnectAsync();
            
            _autoRefreshTimer.Stop();
            Log("Auto-Refresh Stopped.");
            
            Status = "Stopped";
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
            if (_engine == null || _engine.DataService == null)
            {
                Log("DataService not initialized. Cannot fetch option chain.");
                return;
            }

            try
            {
                var chain = await _engine.DataService.BuildOptionChainAsync(index, "WEEKLY");
                if (chain == null) return;

                double spot = await _engine.DataService.GetSpotPriceAsync(index);
                
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    OptionChain.Clear();
                    foreach (var item in chain.OrderBy(i => i.Strike).ThenBy(i => i.OptionType))
                    {
                        // Calculate Greeks (Simplified assumptions)
                        // Time to expiry T (assume 4 days = 4/365)
                        double T = 4.0 / 365.0;
                        double r = 0.07;
                        double sigma = 0.18; // Default vol

                        item.Delta = GreeksCalculator.CalculateDelta(spot, item.Strike, T, r, sigma, item.IsCall);
                        item.Theta = GreeksCalculator.CalculateTheta(spot, item.Strike, T, r, sigma, item.IsCall);
                        item.Vega = GreeksCalculator.CalculateVega(spot, item.Strike, T, r, sigma);

                        OptionChain.Add(item);
                    }
                });
                Log($"✓ Fetched {OptionChain.Count} options for {index}. Greeks calculated.", "SUCCESS");
            }
            catch (Exception ex)
            {
                Log($"Error Fetching Option Chain: {ex.Message}", "ERROR");
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
            // Check if engine and API are initialized
            if (_engine == null || _engine.Api == null) 
            {
                Log("Cannot fetch positions: Trading engine not initialized.");
                return;
            }
            
            try 
            {
                var positions = await _engine.Api.GetPositionAsync();
                
                // Check if positions is null
                if (positions == null)
                {
                    Log("No positions data received from API.");
                    return;
                }
                
                System.Windows.Application.Current.Dispatcher.Invoke(() => 
                {
                    Positions.Clear();
                    foreach(var p in positions)
                    {
                        // Calculate Status based on NetQty
                        int.TryParse(p.NetQty, out int netQty);
                        p.Status = (netQty == 0) ? "CLOSED" : "OPEN";

                        Positions.Add(p);
                    }
                });
                Log($"Fetched {positions.Count} Positions.");
            }
            catch(Exception ex)
            {
                Log($"Error Fetching Positions: {ex.Message}");
            }
        }

        [RelayCommand]
        public async Task FetchOrders()
        {
            if (_engine == null || _engine.Api == null) return;

            try
            {
                var orders = await _engine.Api.GetOrderBookAsync();

                if (orders == null) return;

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
            catch (Exception ex)
            {
                Log($"Error Fetching Orders: {ex.Message}");
            }
        }

        [RelayCommand]
        public async Task<bool> ExitPosition(Position position)
        {
            if (_engine.Api == null)
            {
                Log("Error: Not connected to broker API");
                return false;
            }

            try
            {
                Log($"Exiting position: {position.TradingSymbol}, Qty: {position.NetQty}");

                // Determine the opposite transaction type
                string transactionType;
                int qty = Math.Abs(int.Parse(position.NetQty));

                if (int.Parse(position.NetQty) > 0)
                {
                    // Long position - need to SELL to exit
                    transactionType = "SELL";
                }
                else
                {
                    // Short position - need to BUY to exit
                    transactionType = "BUY";
                }

                // Place market order to exit
                string orderId = await _engine.Api.PlaceOrderAsync(
                    symbol: position.TradingSymbol,
                    token: position.SymbolToken,
                    transactionType: transactionType,
                    qty: qty,
                    price: 0, // Market order
                    variety: "NORMAL",
                    productType: position.ProductType,
                    exchange: position.Exchange
                );

                if (!string.IsNullOrEmpty(orderId))
                {
                    Log($"✓ Exit order placed successfully. Order ID: {orderId}", "SUCCESS");
                    
                    // Update position status
                    position.Status = "EXITED";
                    
                    // Refresh positions after a short delay
                    await Task.Delay(1000);
                    await FetchPositions();
                    
                    return true;
                }
                else
                {
                    Log($"✗ Failed to place exit order for {position.TradingSymbol}", "ERROR");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"Error exiting position: {ex.Message}", "ERROR");
                return false;
            }
        }
    }
}
