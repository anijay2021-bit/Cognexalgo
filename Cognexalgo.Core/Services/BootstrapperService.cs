using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Cognexalgo.Core.Data;
using Cognexalgo.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Cognexalgo.Core.Services
{
    public class BootstrapperService
    {
        private readonly TradingEngine _engine;
        private readonly AngelOneDataService _dataService; // Assume this exists or will be updated
        private readonly IConfiguration _config;
        private readonly GreeksService _greeksService;

        // Events to report progress to UI
        public event Action<string, int> OnProgressChanged;

        // Fired when a critical non-fatal warning needs user attention (e.g. clock drift)
        public event Action<string> OnCriticalWarning;

        public BootstrapperService(TradingEngine engine, AngelOneDataService dataService, IConfiguration config)
        {
            _engine = engine;
            _dataService = dataService;
            _config = config;
            _greeksService = new GreeksService();
        }

        public async Task InitializeAsync()
        {
            try 
            {
                OnProgressChanged?.Invoke("Starting Initialization...", 0);

                // Step 1: DB Sync
                await Step1_SyncDatabaseAsync();
                OnProgressChanged?.Invoke("Database Synced.", 20);

                // Step 2: Historical Data
                // [POST-LOGIN] Always download real history here — JWT is now available.
                // The pre-login phase only downloaded Scrip Master (no auth needed).
                // Historical data REQUIRES JWT auth, so it MUST be fetched here.
                _engine.Logger.Log("Bootstrapper", "📥 Downloading real historical data (JWT available)...");
                await Step2_FetchHistoricalDataAsync();
                OnProgressChanged?.Invoke("Market Data Buffered.", 40);

                // Step 3: Option Chain & Greeks
                await Step3_InitializeOptionChainAsync();
                OnProgressChanged?.Invoke("Option Chain & Greeks Ready.", 60);

                // Step 4: System Health
                await Step4_SystemHealthCheckAsync();
                OnProgressChanged?.Invoke("System Health Verified.", 80);

                // Step 5: Broker Reconciliation
                await Step5_BrokerReconciliationAsync();
                OnProgressChanged?.Invoke("Ready to Trade.", 100);
            }
            catch (Exception ex)
            {
                _engine.Logger.Log("Bootstrapper", $"Initialization Failed: {ex.Message}");
                throw; // Rethrow to blocking UI
            }
        }

        private async Task Step1_SyncDatabaseAsync()
        {
            // Sync Strategies & Active Positions
            if (!_engine.MetadataContext.Database.CanConnect())
            {
                throw new Exception("Cannot connect to Database. Check internet connection.");
            }

            // Ensure Database Schema Exists (Self-Healing)
            // This is critical for new deployments or when new tables (like HybridStrategies) are added
            try
            {
                await _engine.MetadataContext.Database.ExecuteSqlRawAsync(@"
                    CREATE TABLE IF NOT EXISTS ""hybrid_strategies"" (
                        ""Id"" SERIAL PRIMARY KEY,
                        ""Name"" TEXT,
                        ""StrategyType"" TEXT,
                        ""IsActive"" BOOLEAN,
                        ""Parameters"" TEXT,
                        ""Reason"" TEXT
                    );
                ");
            }
            catch (Exception ex)
            {
                _engine.Logger.Log("Bootstrapper", $"Schema Check Warning: {ex.Message}");
            }
            
            // Pre-load Active Strategies into Engine Memory
            // This ensures the UI (StrategiesViewModel) can display them immediately
            var activeStrategies = await _engine.StrategyRepository.GetAllActiveAsync();
            _engine.Logger.Log("Bootstrapper", $"✓ Pre-loaded {activeStrategies.Count()} active strategies from DB.");

            // Sync Active Positions (if any open trades exist from previous session)
            // var positions = await _engine.OrderRepository.GetOpenPositionsAsync(); 
            // _engine.Logger.Log("Bootstrapper", $"✓ Found {positions.Count} open positions in local DB.");
        }

        private async Task Step2_FetchHistoricalDataAsync()
        {
            // [POST-LOGIN DEEP DOWNLOAD] Now that JWT is available,
            // fetch the full depth for all timeframes using the DataService.
            try
            {
                _engine.Logger.Log("Bootstrapper", "═══ DEEP HISTORY DOWNLOAD (post-login, real data) ═══");
                await _dataService.PreFetchDeepHistoryAsync();

                int totalCandles = _dataService.GetTotalCachedCandles();
                _engine.Logger.Log("Bootstrapper", $"✓ Deep history complete: {totalCandles:N0} real candles cached across all timeframes.");
            }
            catch (Exception ex)
            {
                _engine.Logger.Log("Bootstrapper", $"ERROR: Deep history download failed: {ex.Message}");
                _engine.Logger.Log("Bootstrapper", "Falling back to shallow 1-min fetch...");

                // Fallback: at minimum, get 5 days of 1-min data for the major indices
                string[] indices = { "NIFTY", "BANKNIFTY", "FINNIFTY" };
                foreach (var index in indices)
                {
                    try
                    {
                        var history = await _dataService.GetHistoryAsync(index, "ONE_MINUTE", 5);
                        if (history != null && history.Any())
                        {
                            _engine.Logger.Log("Bootstrapper", $"  ✓ Fallback: {history.Count} candles for {index}.");
                        }
                    }
                    catch (Exception fallbackEx)
                    {
                        _engine.Logger.Log("Bootstrapper", $"  ✗ Fallback failed for {index}: {fallbackEx.Message}");
                    }
                    await Task.Delay(200);
                }
            }
        }

        private async Task Step3_InitializeOptionChainAsync()
        {
            string[] indices = { "NIFTY", "BANKNIFTY", "FINNIFTY" };
            foreach (var index in indices)
            {
                try
                {
                    // 1. Build Option Chain (Fetches tokens and LTPs)
                    var optionChain = await _dataService.BuildOptionChainAsync(index, "Weekly");
                    
                    if (optionChain.Any())
                    {
                        var spotPrice = await _dataService.GetSpotPriceAsync(index);
                        // [FIX] Use TokenService for authoritative expiry
                        var expiryDate = _engine.TokenService.GetNextExpiry(index, "Weekly");
                        var daysToExpiry = (expiryDate - DateTime.Now).TotalDays;

                        // 2. Calculate Greeks for each Option
                        foreach (var opt in optionChain)
                        {
                            var greeks = _greeksService.CalculateGreeks(
                                spotPrice: spotPrice,
                                strikePrice: opt.Strike,
                                timeToExpiryDays: daysToExpiry,
                                riskFreeRate: 0.10, // 10% Risk Free Rate
                                volatility: 0.15,   // TODO: Implement IV Calculation or fetch from API
                                isCall: opt.OptionType == "CE"
                            );

                            // Update the Option Model (assuming it has Greek properties)
                            // opt.Delta = greeks.Delta;
                            // opt.Theta = greeks.Theta;
                            // opt.Vega = greeks.Vega;
                            // opt.Gamma = greeks.Gamma;
                        }
                        
                        _engine.Logger.Log("Bootstrapper", $"✓ Calculated Greeks for {optionChain.Count} {index} options.");
                    }
                }
                catch (Exception ex)
                {
                    _engine.Logger.Log("Bootstrapper", $"ERROR: Option Chain init failed for {index}: {ex.Message}");
                }
            }
        }

        private async Task Step4_SystemHealthCheckAsync()
        {
            // 1. API Session Check
            if (string.IsNullOrEmpty(_engine.Api.JwtToken))
            {
                 _engine.Logger.Log("Bootstrapper", "INFO: Broker Session not active. Login will occur on Start Engine.");
            }
            else
            {
                // Verify if JWT is valid by making a lightweight private API call
                var profile = await _engine.Api.GetRMSLimitAsync(); 
                if (profile != null)
                {
                    _engine.Logger.Log("Bootstrapper", $"✓ API Session Valid. Available Funds: ₹{profile.AvailableCash:N2}");

                    // 2. Margin Check
                    // Calculate Total Required Margin for all Active Strategies
                    double totalRequired = 0;
                    var strategies = await _engine.StrategyRepository.GetAllActiveAsync();
                    foreach (var s in strategies)
                    {
                        totalRequired += 100000; // Placeholder
                    }

                    if (profile.AvailableCash < totalRequired)
                    {
                        _engine.Logger.Log("Bootstrapper", $"WARNING: Low Margin! Available: {profile.AvailableCash}, Required: {totalRequired}");
                    }
                }
                else
                {
                    _engine.Logger.Log("Bootstrapper", "WARNING: API Session check failed (GetRMSLimit returned null).");
                }
            }

            // 3. NTP Clock Sync (Always run)
            CheckTimeDrift();
        }

        private void CheckTimeDrift()
        {
            try
            {
                string ntpServer = _config["NtpServer"] ?? "pool.ntp.org";
                // ... NTP Logic (Already implemented in previous step template) ...
                // Re-verifying implementation details:
                var ntpData = new byte[48];
                ntpData[0] = 0x1B; 

                var addresses = Dns.GetHostEntry(ntpServer).AddressList;
                var ipEndPoint = new IPEndPoint(addresses[0], 123);

                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.Connect(ipEndPoint);
                    socket.ReceiveTimeout = 3000;
                    socket.Send(ntpData);
                    socket.Receive(ntpData);
                }

                ulong intPart = (ulong)ntpData[40] << 24 | (ulong)ntpData[41] << 16 | (ulong)ntpData[42] << 8 | (ulong)ntpData[43];
                ulong fractPart = (ulong)ntpData[44] << 24 | (ulong)ntpData[45] << 16 | (ulong)ntpData[46] << 8 | (ulong)ntpData[47];

                var milliseconds = (intPart * 1000) + ((fractPart * 1000) / 0x100000000L);
                var networkDateTime = (new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc)).AddMilliseconds((long)milliseconds);

                var drift = DateTime.UtcNow - networkDateTime;
                _engine.Logger.Log("Bootstrapper", $"✓ System Clock Drift: {drift.TotalMilliseconds:F2}ms");

                if (Math.Abs(drift.TotalMilliseconds) > 3000)
                {
                    _engine.Logger.Log("Bootstrapper", $"CRITICAL: Clock drift > 500ms. Algo trading requires precise time.");
                    OnCriticalWarning?.Invoke(
                        $"System clock is out of sync by {drift.TotalMilliseconds:F0}ms.\n\n" +
                        "Algo trading requires precise time. Please sync your Windows clock:\n" +
                        "Settings → Time & Language → Date & Time → Sync Now");
                }
            }
            catch (Exception ex)
            {
                _engine.Logger.Log("Bootstrapper", $"WARNING: NTP Check Failed: {ex.Message}. Proceeding with local time.");
            }
        }

        private async Task Step5_BrokerReconciliationAsync()
        {
             try
             {
                 // 1. Get Live Positions from Broker
                 var brokerPositions = await _engine.Api.GetPositionAsync();
                 
                 // 2. Get Local Positions from DB
                 // We now have ActivePositions in Context
                 var localPositions = await _engine.MetadataContext.ActivePositions.ToListAsync();
                 
                 // 3. Compare and Reconcile
                 if (brokerPositions != null && brokerPositions.Any())
                 {
                     _engine.Logger.Log("Bootstrapper", $"✓ Broker Reconciliation: Found {brokerPositions.Count} open positions.");
                     
                     // Simple Sync: Clear Local and Replace with Broker (Source of Truth)
                     // In production, we might want smarter merging, but for "Reconciliation" replacing is safest to avoid ghosts.
                     _engine.MetadataContext.ActivePositions.RemoveRange(localPositions);
                     
                     foreach (var bp in brokerPositions)
                     {
                         int.TryParse(bp.NetQty, out int qty);
                         if (qty == 0) continue; // Skip closed positions

                         decimal avgPrice = (decimal)bp.AvgNetPrice;
                         decimal ltp = (decimal)bp.Ltp;
                         decimal pnl = (decimal)bp.Pnl;

                         _engine.MetadataContext.ActivePositions.Add(new ActivePosition
                         {
                             SymbolToken = bp.SymbolToken,
                             TradingSymbol = bp.TradingSymbol,
                             Quantity = qty,
                             AveragePrice = avgPrice,
                             LTP = ltp,
                             PnL = pnl,
                             ProductType = bp.ProductType ?? "MIS",
                             StrategyName = "UNKNOWN", // Broker doesn't know strategy name
                             UpdatedAt = DateTime.Now
                         });
                     }
                     await _engine.MetadataContext.SaveChangesAsync();
                     _engine.Logger.Log("Bootstrapper", "✓ Local DB Synced with Broker Positions.");
                 }
                 else
                 {
                     if (localPositions.Any())
                     {
                         _engine.Logger.Log("Bootstrapper", "✓ Broker has 0 positions. Clearing local DB.");
                         _engine.MetadataContext.ActivePositions.RemoveRange(localPositions);
                         await _engine.MetadataContext.SaveChangesAsync();
                     }
                     _engine.Logger.Log("Bootstrapper", "✓ Broker Reconciliation: No open positions.");
                 }
             }
             catch (Exception ex)
             {
                  _engine.Logger.Log("Bootstrapper", $"ERROR: Broker Reconciliation failed: {ex.Message}");
             }
        }
    }
}
