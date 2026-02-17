using System.Linq;
using System.Threading.Tasks;
using Cognexalgo.Core.Services;
using Cognexalgo.Core.Database;
using Cognexalgo.Core.Repositories;
using Cognexalgo.Core.CloudServices;
using Newtonsoft.Json;
using Cognexalgo.Core.Rules;
using Cognexalgo.Core.Models;
using Microsoft.Extensions.Configuration; // [NEW]
using Microsoft.EntityFrameworkCore;    // [NEW]
using Cognexalgo.Core.Data;             // [NEW]

namespace Cognexalgo.Core
{
    public class TradingEngine
    {
        // Global Signal Event
        public event Action<Signal> OnSignalReceived;

        public TickerService Ticker { get; private set; }
        public SmartApiClient Api { get; private set; }
        public TokenService TokenService { get; private set; }
        public AngelOneDataService DataService { get; private set; } // [NEW] Real-time data service

        public bool IsRunning { get; private set; }
        public bool IsPaperTrading { get; set; } = true; // Default to Paper Logic
        
        // RMS Limits
        public double MaxLoss { get; private set; } = -50000;
        public double MaxProfit { get; private set; } = 50000;

        public IStrategyRepository StrategyRepository { get; private set; }
        public IOrderRepository OrderRepository { get; private set; }
        public CredentialsRepository CredentialsRepository { get; private set; }
        
        public FileLoggingService Logger { get; private set; } // [NEW] Logger
        
        private TotpService _totpService; // [NEW] TOTP Handler

        public AlgoDbContext MetadataContext { get; private set; } // [NEW] EF Core Context
        
        private DatabaseService _dbService; // [FIX] Added missing field
        private IFirebaseService _firebaseService; // [FIX] Added missing field
        private string _clientId; // [FIX] Added missing field
        
        // [NEW] Track active strategies for PnL aggregation
        private List<Cognexalgo.Core.Strategies.StrategyBase> _activeStrategies = new List<Cognexalgo.Core.Strategies.StrategyBase>();

        public TradingEngine()
        {
            // Load Configuration
            var config = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();

            // Initialize EF Core Context
            var connectionString = config.GetConnectionString("AlgoDatabase");
            var optionsBuilder = new DbContextOptionsBuilder<AlgoDbContext>();
            
            if (!string.IsNullOrEmpty(connectionString))
            {
                optionsBuilder.UseNpgsql(connectionString);
            }
            else 
            {
                // Fallback (or throw) - prevents crash if config missing in tests
                // But ideally should throw if DB is required
                optionsBuilder.UseNpgsql("Host=localhost;Database=algopro;Username=postgres;Password=password");
            }
            
            MetadataContext = new AlgoDbContext(optionsBuilder.Options);
            
            // Legacy SQLite Service (keep for Order/Auth repos if they are not migrated yet)
            _dbService = new DatabaseService();
            
            // Repositories
            StrategyRepository = new StrategyRepository(MetadataContext);
            OrderRepository = new OrderRepository(_dbService);
            CredentialsRepository = new CredentialsRepository(_dbService);

            TokenService = new TokenService();
            Ticker = new TickerService("wss://smartapi.angelbroking.com/websocket");
            
            // Initialize Logger
            Logger = new FileLoggingService();
            _totpService = new TotpService();
            
            _clientId = Environment.MachineName;
            Logger.Log("Engine", "Trading Engine Constructed.");

            _clientId = Environment.MachineName;
            Logger.Log("Engine", "Trading Engine Constructed.");
        }

        public async Task InitializeDatabaseAsync()
        {
             // Ensure database is created (Migration substitute for now)
             await MetadataContext.Database.EnsureCreatedAsync();

             // [NEW] Migrate Strategies from SQLite to Postgres if Postgres is empty
             try 
             {
                 var postgresCount = await MetadataContext.HybridStrategies.CountAsync();
                 if (postgresCount == 0)
                 {
                     Logger.Log("Migration", "Postgres empty. Checking for local SQLite strategies...");
                     using (var conn = _dbService.GetConnection())
                     {
                         await conn.OpenAsync();
                         var cmd = conn.CreateCommand();
                         cmd.CommandText = "SELECT Name, Symbol, StrategyType, Parameters, IsActive, ProductType FROM Strategies";
                         using (var reader = await cmd.ExecuteReaderAsync())
                         {
                             while (await reader.ReadAsync())
                             {
                                 var name = reader.GetString(0);
                                 var symbol = reader.GetString(1);
                                 var type = reader.GetString(2);
                                 var paramJson = reader.IsDBNull(3) ? "{}" : reader.GetString(3);
                                 var isActive = reader.GetInt32(4) == 1;
                                 var productType = reader.IsDBNull(5) ? "MIS" : reader.GetString(5);

                                 Logger.Log("Migration", $"Migrating {name}...");

                                 // Basic Migration Mapping
                                 var config = new HybridStrategyConfig
                                 {
                                     Name = name,
                                     StrategyType = type,
                                     IsActive = isActive,
                                     ProductType = productType,
                                     Legs = new List<StrategyLeg> 
                                     { 
                                         new StrategyLeg { Index = symbol, Status = "PENDING" } 
                                     }
                                 };

                                 await StrategyRepository.SaveHybridStrategyAsync(config, "Migration");
                             }
                         }
                     }
                     Logger.Log("Migration", "Successfully migrated local strategies to Cloud.");
                 }

                 // [NEW] Migrate Accounts from SQLite to Postgres
                 var accountCount = await MetadataContext.AccountConfigs.CountAsync();
                 if (accountCount == 0)
                 {
                     Logger.Log("Migration", "Postgres accounts empty. Checking SQLite...");
                     using (var conn = _dbService.GetConnection())
                     {
                         await conn.OpenAsync();
                         var cmd = conn.CreateCommand();
                         cmd.CommandText = "SELECT BrokerName, ApiKey, ClientCode, TotpKey FROM Credentials";
                         using (var reader = await cmd.ExecuteReaderAsync())
                         {
                             while (await reader.ReadAsync())
                             {
                                 var broker = reader.GetString(0);
                                 var apiKey = reader.IsDBNull(1) ? "" : reader.GetString(1);
                                 var clientCode = reader.GetString(2);
                                 var totp = reader.IsDBNull(3) ? "" : reader.GetString(3);

                                 if (string.IsNullOrEmpty(clientCode)) continue;

                                 var acc = new AccountConfig
                                 {
                                     ClientId = clientCode,
                                     AccountName = $"{broker}_{clientCode}",
                                     Broker = broker,
                                     Status = "Offline",
                                     IsEnabled = true,
                                     // Description = $"Migrated from local"
                                 };

                                 MetadataContext.AccountConfigs.Add(acc);
                             }
                             await MetadataContext.SaveChangesAsync();
                         }
                     }
                     Logger.Log("Migration", "Successfully migrated local accounts to Cloud.");
                 }
             }
             catch (Exception ex)
             {
                 Logger.Log("Migration", $"Error during migration: {ex.Message}");
             }
        }

        public void SetCloudService(IFirebaseService firebaseService)
        {
            _firebaseService = firebaseService;
            Task.Run(() => StartRemoteListenerAsync());
            Task.Run(() => StartHeartbeatLoopAsync());
            Logger.Log("Engine", "Cloud Service Attached.");
        }

        public void UpdateRmsLimits(double maxLoss, double maxProfit)
        {
            MaxLoss = maxLoss;
            MaxProfit = maxProfit;
            Logger.Log("RMS", $"Limits Updated: MaxLoss={MaxLoss}, MaxProfit={MaxProfit}");
        }

        private async Task StartHeartbeatLoopAsync()
        {
            while (true)
            {
                if (_firebaseService != null)
                {
                    var heartbeat = new Cognexalgo.Core.Models.ClientHeartbeat
                    {
                        ClientId = _clientId,
                        LastUpdated = DateTime.UtcNow,
                        Status = IsRunning ? "RUNNING" : "STOPPED",
                        TotalPnL = (decimal)GetTotalPnL(),
                        ActiveStrategiesCount = 0 // Update with real count
                    };
                    await _firebaseService.PushHeartbeatAsync(heartbeat);
                }
                await Task.Delay(5000);
            }
        }

        public double GetTotalPnL()
        {
            if (_activeStrategies == null || !_activeStrategies.Any()) return 0.0;
            return (double)_activeStrategies.Sum(s => s.Pnl);
        }

        private async Task StartRemoteListenerAsync()
        {
            while (true)
            {
                if (_firebaseService != null)
                {
                    var command = await _firebaseService.GetLatestCommandAsync(_clientId);
                    if (command != null)
                    {
                        Logger.Log("Cloud", $"Received Command: {command.Action}"); 
                        if (command.Action == "STOP")
                        {
                            Stop();
                        }
                        else if (command.Action == "SQUARE_OFF") 
                        {
                             SquareOffAll();
                             Stop();
                        }
                    }
                }
                await Task.Delay(5000); 
            }
        }

        public async Task SquareOffAll()
        {
            Logger.Log("Engine", "ALERT: Global Square-Off Triggered.");
            
            if (Api == null)
            {
                Logger.Log("Engine", "ERROR: API not connected. Cannot Square Off.");
                return;
            }

            try 
            {
                var positions = await Api.GetPositionAsync();
                if (positions == null || !positions.Any()) return;

                foreach (var pos in positions)
                {
                    int.TryParse(pos.NetQty, out int qty);
                    if (qty == 0) continue;

                    string action = qty > 0 ? "SELL" : "BUY";
                    int absQty = Math.Abs(qty);

                    Logger.Log("Engine", $"Squaring Off: {pos.TradingSymbol} | Qty: {absQty} | Action: {action}");
                    
                    await Api.PlaceOrderAsync(
                        symbol: pos.TradingSymbol,
                        token: pos.SymbolToken,
                        transactionType: action,
                        qty: absQty,
                        price: 0, // Market
                        variety: "NORMAL",
                        productType: pos.ProductType,
                        exchange: pos.Exchange
                    );
                }
                Logger.Log("Engine", "SUCCESS: All positions squared off.");
            }
            catch (Exception ex)
            {
                Logger.Log("Engine", $"ERROR during Square-Off: {ex.Message}");
            }
        }

        /// <summary>
        /// Connect to Angel One API with credentials
        /// </summary>
        public async Task<bool> ConnectAsync(string apiKey, string clientCode, string password, string totpSecret)
        {
            try
            {
                Logger?.Log("Engine", "Connecting to Angel One API...");

                // Initialize API client
                Api = new SmartApiClient(apiKey);

                // Generate TOTP
                string totp = _totpService.GenerateTotp(totpSecret);

                // Login
                bool loginSuccess = await Api.LoginAsync(clientCode, password, totp);

                if (!loginSuccess || string.IsNullOrEmpty(Api.JwtToken))
                {
                    Logger?.Log("Engine", "ERROR: Login failed - no JWT token received");
                    return false;
                }

                Logger?.Log("Engine", $"✓ Connected successfully. Feed Token: {Api.FeedToken}");

                // CRITICAL FIX: Properly await LoadMasterAsync to prevent race condition
                // Previously used fire-and-forget (_ = ...) which caused searches to run before loading completed
                Logger?.Log("Engine", "Loading Scrip Master...");
                await TokenService.LoadMasterAsync();
                Logger?.Log("Engine", $"✓ Scrip Master loaded. Total symbols: {TokenService.GetSymbolCount()}");

                // Initialize DataService with the API client and TokenService
                DataService = new AngelOneDataService(Api, TokenService, Logger);

                // [NEW] Global History Fetch for Indices
                _ = Task.Run(async () => {
                    try {
                        await DataService.PreFetchGlobalHistoryAsync();
                    } catch (Exception ex) {
                        Logger?.Log("Engine", $"Global Pre-fetch Failed: {ex.Message}");
                    }
                });

                return true;
            }
            catch (Exception ex)
            {
                Logger?.Log("Engine", $"ERROR: Connection failed - {ex.Message}");
                return false;
            }
        }

        public async Task Start()
        {
            if (IsRunning) return;
            IsRunning = true;
            Logger.Log("Engine", "Engine Started.");

            // Load Strategies
            var strategies = await StrategyRepository.GetAllActiveAsync();
            _activeStrategies.Clear(); // Reset list on start

            Console.WriteLine($"Found {strategies.Count()} active strategies.");
            Logger.Log("Engine", $"Found {strategies.Count()} active strategies.");

            foreach (var config in strategies)
            {
                try 
                {
                    if (config.StrategyType == "CUSTOM")
                    {
                        var strategy = new Cognexalgo.Core.Strategies.DynamicStrategy(this, config.Parameters);
                        
                        // [NEW] Fetch History for Technical Indicators (e.g. 200 EMA)
                        if (DataService != null)
                        {
                            // Fetch 2 days of history to ensure 200 EMA has enough data
                            var history = await DataService.GetHistoryAsync(config.Symbol, "ONE_MINUTE", 2);
                            await strategy.InitializeAsync(history);
                        }

                        strategy.IsActive = true;
                        strategy.OnSignalGenerated += (s) => OnSignalReceived?.Invoke(s);
                        _activeStrategies.Add(strategy);
                    }
                    else if (config.StrategyType == "HYBRID")
                    {
                        var strategy = new Cognexalgo.Core.Strategies.HybridStraddleStrategy(this);
                        strategy.Initialize(config.Parameters);
                        strategy.IsActive = true;
                        strategy.OnSignalGenerated += (s) => OnSignalReceived?.Invoke(s);
                        _activeStrategies.Add(strategy);
                    }
                    else if (config.StrategyType == "CALENDAR")
                    {
                        var strategy = new Cognexalgo.Core.Strategies.CalendarStrategy(this, config.Parameters);
                        strategy.IsActive = true;
                        strategy.OnSignalGenerated += (s) => OnSignalReceived?.Invoke(s);
                        _activeStrategies.Add(strategy);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to load strategy {config.Name}: {ex.Message}");
                    Logger.Log("Engine", $"Failed to load strategy {config.Name}: {ex.Message}");
                }
            }

            _ = Task.Run(async () => 
            {
                Logger.Log("Engine", "Starting Live Market Data Loop...");
                Console.WriteLine("DEBUG: Starting Live Market Data Loop..."); // FORCE CONSOLE OUT
                
                // Initialize cache to avoid 0s on first tick failure
                double lastNifty = 0;
                double lastBankNifty = 0;
                double lastFinNifty = 0;
                double lastMidcpNifty = 0;
                double lastSensex = 0;

                while (IsRunning)
                {
                    try
                    {
                        // 1. Fetch Real-Time Spot Prices
                        try { lastNifty = await DataService.GetSpotPriceAsync("NIFTY"); } catch (Exception ex) { Logger.Log("Engine", $"Nifty Fetch Failed: {ex.Message}"); }
                        try { lastBankNifty = await DataService.GetSpotPriceAsync("BANKNIFTY"); } catch (Exception ex) { Logger.Log("Engine", $"BankNifty Fetch Failed: {ex.Message}"); }
                        try { lastFinNifty = await DataService.GetSpotPriceAsync("FINNIFTY"); } catch (Exception ex) { Logger.Log("Engine", $"FinNifty Fetch Failed: {ex.Message}"); }

                        // 2. Create Ticker Data
                        var data = new TickerData 
                        { 
                            Nifty = new InstrumentInfo { Ltp = lastNifty }, 
                            BankNifty = new InstrumentInfo { Ltp = lastBankNifty },
                            FinNifty = new InstrumentInfo { Ltp = lastFinNifty },
                        };

                        // 3. Emit to UI
                        Ticker.EmitTick(data);

                        // [NEW] Check RMS Limits
                        double currentMtm = GetTotalPnL(); 
                        if (currentMtm <= MaxLoss)
                        {
                            Logger.Log("RMS", $"[CRITICAL] Max Loss Reached ({currentMtm}). Triggering Global Square-Off.");
                            await SquareOffAll();
                            Stop();
                        }
                        else if (currentMtm >= MaxProfit)
                        {
                            Logger.Log("RMS", $"[SUCCESS] Max Profit Reached ({currentMtm}). Triggering Global Square-Off.");
                            await SquareOffAll();
                            Stop();
                        }

                        // 4. Update Strategies
                        foreach (var strategy in _activeStrategies.ToList()) 
                        {
                            try 
                            {
                                await strategy.OnTickAsync(data);
                                strategy.RecalculatePnl(data); 
                            }
                            catch (Exception ex)
                            {
                                Logger.Log("Engine", $"Error in strategy {strategy.Name}: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                         Logger.Log("Engine", $"Tick Loop Error: {ex.Message}");
                    }

                    await Task.Delay(1000); // 1s Loop
                }
                 Logger.Log("Engine", "Engine Execution Loop Stopped.");
            });
        }

        public void Stop()
        {
            IsRunning = false;
            Logger.Log("Engine", "Engine Stop Requested.");
        }

        public async Task ExecuteOrderAsync(StrategyConfig strategy, string symbol, string action, int explicitQty = 0, double triggerPrice = 0, double potentialProfit = 0, double protectedProfit = 0)
        {
            try 
            {
                // 1. Determine Transaction Type
                string transactionType = "BUY";
                if (action.Contains("SELL") || action == "EXIT") transactionType = "SELL";

                // 2. Determine Token (Mock or Real)
                string token = "12345"; // Default mock
                
                // [NEW] Resolve Index to Option if action is BUY_CE/PE
                if ((action == "BUY_CE" || action == "BUY_PE") && (symbol == "NIFTY" || symbol == "BANKNIFTY" || symbol == "FINNIFTY"))
                {
                    string optionType = action.EndsWith("CE") ? "CE" : "PE";
                    var (atmToken, atmSymbol) = await TokenService.GetAtmOptionAsync(symbol, optionType, DataService);
                    if (!string.IsNullOrEmpty(atmToken))
                    {
                        token = atmToken;
                        symbol = atmSymbol; 
                        Console.WriteLine($"[Execution] Resolved {action} for Index -> ATM Option {symbol} ({token})");
                    }
                }

                // 3. Get Instrument Info (Token + Lot Size)
                var (instrumentToken, lotSize) = TokenService.GetInstrumentInfo(symbol);
                if (!string.IsNullOrEmpty(instrumentToken))
                {
                    token = instrumentToken; 
                }

                // 4. Calculate Quantity
                int qty = explicitQty;
                int userLots = 1;
                if (qty <= 0)
                {
                    if (strategy.StrategyType == "CUSTOM" && !string.IsNullOrEmpty(strategy.Parameters))
                    {
                        try 
                        {
                            var dc = JsonConvert.DeserializeObject<Cognexalgo.Core.Rules.DynamicStrategyConfig>(strategy.Parameters);
                            userLots = dc?.TotalLots ?? 1;
                        } catch {}
                    }
                    qty = userLots * (lotSize > 0 ? lotSize : 1);
                }
                
                double executionPrice = 0;

                string orderId = null;
                if (IsPaperTrading)
                {
                    // For performance analytics in paper trading, we use the trigger price or current market price
                    executionPrice = triggerPrice > 0 ? triggerPrice : 0; 
                    orderId = "PAPER_" + Guid.NewGuid().ToString().Substring(0, 8);
                }
                else
                {
                    orderId = await Api.PlaceOrderAsync(symbol, token, transactionType, qty, 0, "NORMAL", "MIS");
                    // In real execution, we would ideally fetch the average trade price from the API later
                    executionPrice = 0; 
                }

                if (!string.IsNullOrEmpty(orderId)) 
                {
                   Logger.Log("Order", $"Order Placed: {orderId} | Symbol: {symbol} | Qty: {qty} | Strategy: {strategy.Name}");
                   
                   var order = new Order
                   {
                       OrderId = orderId,
                       StrategyId = strategy.Id,
                       StrategyName = strategy.Name,
                       Symbol = symbol,
                       Token = token,
                       TransactionType = transactionType,
                       Qty = qty,
                       Price = executionPrice, 
                       Status = "OPEN",
                       Timestamp = DateTime.Now,
                       
                       // Phase 3: Analytics
                       PotentialProfit = potentialProfit,
                       ProtectedProfit = protectedProfit,
                       ActualProfit = 0 // Will be updated on EXITED
                   };
                   await OrderRepository.AddAsync(order);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Execution Error: {ex.Message}");
                Logger.Log("Order", $"Execution Failed: {ex.Message}");
            }
        }

        #region Hybrid Strategy Execution

        /// <summary>
        /// Executes a Hybrid strategy with multiple legs
        /// </summary>
        public async Task ExecuteHybridStrategy(HybridStrategyConfig config, StrategyConfig strategyConfig)
        {
            try
            {
                Logger.Log("Hybrid", $"Executing Hybrid Strategy: {config.Name} with {config.Legs.Count} legs");

                foreach (var leg in config.Legs)
                {
                    await ExecuteLeg(leg, strategyConfig);
                    
                    // Small delay between legs to avoid rate limiting
                    await Task.Delay(500);
                }

                Logger.Log("Hybrid", $"Hybrid Strategy {config.Name} execution completed");
            }
            catch (Exception ex)
            {
                Logger.Log("Hybrid", $"Error executing Hybrid Strategy: {ex.Message}");
                Console.WriteLine($"[ERROR] Hybrid Strategy Execution Failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Executes a single leg of a Hybrid strategy
        /// </summary>
        public async Task<string> ExecuteLeg(StrategyLeg leg, StrategyConfig strategyConfig)
        {
            try
            {
                // 1. Get spot price for the index
                double spotPrice = await GetSpotPriceWithRetry(leg.Index);
                Logger.Log("Hybrid", $"Spot Price for {leg.Index}: ₹{spotPrice:N2}");

                // 2. Build option chain with real LTP data (with retry logic)
                var chain = await BuildOptionChainWithRetry(leg.Index, leg.ExpiryType);
                
                if (chain == null || chain.Count == 0)
                {
                    Logger.Log("Hybrid", $"ERROR: Failed to build option chain for {leg.Index}. Skipping leg.");
                    return "FAILED";
                }

                Logger.Log("Hybrid", $"Option chain built: {chain.Count} options available");

                // 3. Calculate target strike
                int targetStrike = leg.GetTargetStrike(spotPrice, chain);

                if (targetStrike == 0 && leg.Mode == StrikeSelectionMode.ClosestPremium && leg.WaitForMatch)
                {
                    Logger.Log("Hybrid", $"Closest Premium {leg.TargetPremium} not found within tolerance. Waiting...");
                    return "PENDING";
                }
                else if (targetStrike == 0)
                {
                    Logger.Log("Hybrid", $"No matching strike found for Closest Premium {leg.TargetPremium}. Skipping.");
                    return "SKIPPED"; // Or FAILED based on preference
                }

                leg.CalculatedStrike = targetStrike;

                leg.CalculatedStrike = targetStrike;
                
                // 4. Find matching Option from Chain (Instead of rebuilding Symbol)
                string optTypeStr = leg.OptionType == OptionType.Call ? "CE" : "PE";
                var selectedOption = chain.FirstOrDefault(x => x.Strike == targetStrike && x.OptionType == optTypeStr);

                if (selectedOption == null)
                {
                    Logger.Log("Hybrid", $"ERROR: Option not found in chain for Strike {targetStrike} {optTypeStr}");
                    return "FAILED";
                }

                string symbol = selectedOption.Symbol;
                string token = selectedOption.Token;
                int lotSize = selectedOption.LotSize;

                Logger.Log("Hybrid", $"Selected Option: {symbol} (Token: {token}, LotSize: {lotSize})");
                
                leg.SymbolToken = token;
                leg.EntryPrice = selectedOption.LTP; 
                leg.EntryIndexLtp = spotPrice;       // Capture Underlying Spot for Simulation
                leg.Ltp = selectedOption.LTP;        
                leg.EntryTime = DateTime.Now;

                // 5. Calculate quantity
                int qty = leg.TotalLots * lotSize;
                Logger.Log("Hybrid", $"Quantity: {leg.TotalLots} lots × {lotSize} lot size = {qty} contracts");

                // 7. Map product type
                string productType = leg.ProductType == "MIS" ? "INTRADAY" : "CARRYFORWARD";

                // 8. Determine transaction type
                string transactionType = leg.Action == ActionType.Buy ? "BUY" : "SELL";

                // 9. Place order with Slippage Management (0.5% Buffer)
                string orderId = null;
                double price = 0; 
                
                // Calculate Limit Price Buffer
                double buffer = selectedOption.LTP * 0.005; // 0.5%
                if (leg.Action == ActionType.Buy)
                {
                    price = selectedOption.LTP + buffer; // Buy higher to ensure fill
                    // Round up to nearest 0.05
                    price = Math.Ceiling(price / 0.05) * 0.05;
                }
                else
                {
                    price = selectedOption.LTP - buffer; // Sell lower to ensure fill
                    // Round down to nearest 0.05
                    price = Math.Floor(price / 0.05) * 0.05;
                }

                Logger.Log("Hybrid", $"Slippage Mgmt: LTP={selectedOption.LTP}, Buffer={buffer:N2}, LimitPrice={price:N2}");

                if (IsPaperTrading)
                {
                    Console.WriteLine($"[PAPER] {transactionType} {qty} × {symbol} @ Limit {price:N2} (LTP: {selectedOption.LTP})");
                    orderId = "PAPER_" + Guid.NewGuid().ToString().Substring(0, 8);
                }
                else
                {
                    // Pass calculated Limit Price instead of 0 (Market)
                    orderId = await Api.PlaceOrderAsync(symbol, token, transactionType, qty, price, "NORMAL", productType);
                }

                if (!string.IsNullOrEmpty(orderId))
                {
                    Console.WriteLine($"Order Placed: {orderId}");
                    Logger.LogPosition(symbol, transactionType, qty, price, orderId);

                    // Save to database
                    var order = new Order
                    {
                        OrderId = orderId,
                        StrategyId = strategyConfig.Id,
                        StrategyName = strategyConfig.Name,
                        Symbol = symbol,
                        Token = token,
                        TransactionType = transactionType,
                        Qty = qty,
                        Price = price,
                        Status = "OPEN",
                        Timestamp = DateTime.Now
                    };
                    await OrderRepository.AddAsync(order);
                }

                return orderId;
            }
            catch (Exception ex)
            {
                Logger.Log("Hybrid", $"Error executing leg: {ex.Message}");
                Console.WriteLine($"[ERROR] Leg Execution Failed: {ex.Message}");
                return "ERROR";
            }
        }

        /// <summary>
        /// Gets current spot price for an index
        /// </summary>
        private async Task<double> GetSpotPrice(string index)
        {
            if (DataService == null) return 0;
            return await DataService.GetSpotPriceAsync(index);
        }

        /// <summary>
        /// Builds a simplified option chain from Scrip Master
        /// Builds option symbol name
        /// </summary>


        /// <summary>
        /// Get spot price with retry logic for API failures
        /// </summary>
        private async Task<double> GetSpotPriceWithRetry(string index, int maxRetries = 3)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    return await DataService.GetSpotPriceAsync(index);
                }
                catch (Exception ex)
                {
                    Logger.Log("DataService", $"WARNING: Spot price fetch attempt {attempt}/{maxRetries} failed: {ex.Message}");
                    
                    if (attempt < maxRetries)
                    {
                        Logger.Log("DataService", "Retrying after 1 second...");
                        await Task.Delay(1000); // Wait 1 second before retry
                    }
                    else
                    {
                        Logger.Log("DataService", $"ERROR: Failed to fetch spot price after {maxRetries} attempts");
                        throw;
                    }
                }
            }
            
            throw new Exception("Unexpected error in GetSpotPriceWithRetry");
        }

        /// <summary>
        /// Build option chain with retry logic for API failures
        /// </summary>
        private async Task<List<OptionChainItem>> BuildOptionChainWithRetry(string index, string expiryType, int maxRetries = 3)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var chain = await DataService.BuildOptionChainAsync(index, expiryType);
                    
                    // Validate that we got meaningful data
                    if (chain == null || chain.Count == 0)
                    {
                        Logger.Log("DataService", $"WARNING: Option chain attempt {attempt}/{maxRetries} returned empty data");
                        
                        if (attempt < maxRetries)
                        {
                            Logger.Log("DataService", "Retrying after 1 second...");
                            await Task.Delay(1000);
                            continue;
                        }
                        else
                        {
                            Logger.Log("DataService", $"ERROR: Failed to build option chain after {maxRetries} attempts");
                            return null;
                        }
                    }
                    
                    return chain;
                }
                catch (Exception ex)
                {
                    Logger.Log("DataService", $"WARNING: Option chain fetch attempt {attempt}/{maxRetries} failed: {ex.Message}");
                    
                    if (attempt < maxRetries)
                    {
                        Logger.Log("DataService", "Retrying after 1 second...");
                        await Task.Delay(1000);
                    }
                    else
                    {
                        Logger.Log("DataService", $"ERROR: Failed to build option chain after {maxRetries} attempts");
                        return null;
                    }
                }
            }
            
            return null;
        }

        #endregion
    }
}
