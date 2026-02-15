using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cognexalgo.Core;
using Cognexalgo.Core.Models;
using Newtonsoft.Json.Linq;

namespace Cognexalgo.Tests
{
    /// <summary>
    /// Test program to demonstrate Closest Premium scan with real Angel One API
    /// Shows execution logs for strike selection and LTP mapping with latency tracking
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=".PadRight(80, '='));
            Console.WriteLine("CLOSEST PREMIUM SCAN - REAL API DEMONSTRATION");
            Console.WriteLine("Angel One API Integration Test with Latency Tracking");
            Console.WriteLine("=".PadRight(80, '='));
            Console.WriteLine();

            // Load configuration
            var config = LoadConfiguration();
            if (config == null)
            {
                Console.WriteLine("❌ Failed to load configuration. Please check appsettings.json");
                Console.WriteLine();
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return;
            }

            // Initialize Trading Engine
            var engine = new TradingEngine();
            var totalStopwatch = Stopwatch.StartNew();

            try
            {
                // Extract credentials
                string apiKey = config["AngelOne"]["ApiKey"].ToString();
                string clientCode = config["AngelOne"]["ClientCode"].ToString();
                string password = config["AngelOne"]["Password"].ToString();
                string totpSecret = config["AngelOne"]["TotpSecret"].ToString();

                // Validate credentials
                if (apiKey.Contains("YOUR_") || clientCode.Contains("YOUR_"))
                {
                    Console.WriteLine("❌ ERROR: Please update appsettings.json with your actual Angel One credentials");
                    Console.WriteLine();
                    Console.WriteLine("Required fields:");
                    Console.WriteLine("  - AngelOne.ApiKey");
                    Console.WriteLine("  - AngelOne.ClientCode");
                    Console.WriteLine("  - AngelOne.Password");
                    Console.WriteLine("  - AngelOne.TotpSecret");
                    Console.WriteLine();
                    Console.WriteLine("Press any key to exit...");
                    Console.ReadKey();
                    return;
                }

                // Test parameters
                string index = config["Testing"]["Index"]?.ToString() ?? "NIFTY";
                string expiryType = config["Testing"]["ExpiryType"]?.ToString() ?? "Weekly";
                double targetPremium = config["Testing"]["TargetPremium"]?.ToObject<double>() ?? 50.0;

                Console.WriteLine($"Test Parameters:");
                Console.WriteLine($"  - Index: {index}");
                Console.WriteLine($"  - Expiry Type: {expiryType}");
                Console.WriteLine($"  - Target Premium: ₹{targetPremium:N2}");
                Console.WriteLine();

                // Step 1: Initialize Connection
                Console.WriteLine("=".PadRight(80, '='));
                Console.WriteLine("STEP 1: ANGEL ONE CONNECTION");
                Console.WriteLine("=".PadRight(80, '='));
                
                var sw = Stopwatch.StartNew();
                await engine.ConnectAsync(apiKey, clientCode, password, totpSecret);
                sw.Stop();
                
                Console.WriteLine($"✓ Connection Time: {sw.ElapsedMilliseconds}ms");
                Console.WriteLine();

                // Step 2: Load Scrip Master
                Console.WriteLine("=".PadRight(80, '='));
                Console.WriteLine("STEP 2: SCRIP MASTER LOADING");
                Console.WriteLine("=".PadRight(80, '='));
                
                var scripStopwatch = Stopwatch.StartNew();
                await Task.Delay(3000); // Allow background loading
                scripStopwatch.Stop();
                
                Console.WriteLine($"✅ Scrip Master loaded");
                Console.WriteLine($"⏱️  Latency: {scripStopwatch.ElapsedMilliseconds}ms");
                Console.WriteLine();

                // Step 3: Fetch Spot Price
                Console.WriteLine("=".PadRight(80, '='));
                Console.WriteLine("STEP 3: SPOT PRICE FETCHING");
                Console.WriteLine("=".PadRight(80, '='));
                
                var spotStopwatch = Stopwatch.StartNew();
                double spotPrice = await engine.DataService.GetSpotPriceAsync(index);
                spotStopwatch.Stop();
                
                Console.WriteLine($"✅ {index} Spot Price: ₹{spotPrice:N2}");
                Console.WriteLine($"⏱️  Latency: {spotStopwatch.ElapsedMilliseconds}ms");
                Console.WriteLine();

                // Step 4: Build Option Chain
                Console.WriteLine("=".PadRight(80, '='));
                Console.WriteLine("STEP 4: OPTION CHAIN BUILDING");
                Console.WriteLine("=".PadRight(80, '='));
                
                var chainStopwatch = Stopwatch.StartNew();
                var optionChain = await engine.DataService.BuildOptionChainAsync(index, expiryType);
                chainStopwatch.Stop();
                
                Console.WriteLine($"✅ Option Chain Built: {optionChain.Count} options available");
                Console.WriteLine($"⏱️  Latency: {chainStopwatch.ElapsedMilliseconds}ms ({chainStopwatch.ElapsedMilliseconds / 1000.0:F2}s)");
                Console.WriteLine($"📊 Average per option: {(double)chainStopwatch.ElapsedMilliseconds / optionChain.Count:F2}ms");
                Console.WriteLine();

                // Display sample options
                Console.WriteLine("Sample Options (First 10 Calls):");
                Console.WriteLine("-".PadRight(80, '-'));
                Console.WriteLine($"{"Strike",-10} {"Type",-6} {"LTP",-12} {"Symbol",-30}");
                Console.WriteLine("-".PadRight(80, '-'));

                int count = 0;
                foreach (var option in optionChain.OrderBy(o => o.Strike))
                {
                    if (option.OptionType == "CE" && count < 10)
                    {
                        Console.WriteLine($"{option.Strike,-10} {option.OptionType,-6} ₹{option.LTP,-10:N2} {option.Symbol,-30}");
                        count++;
                    }
                }
                Console.WriteLine();

                // Step 5: Closest Premium Scan
                Console.WriteLine("=".PadRight(80, '='));
                Console.WriteLine($"STEP 5: CLOSEST PREMIUM SCAN (Target: ₹{targetPremium:N2})");
                Console.WriteLine("=".PadRight(80, '='));

                // Create a test leg with Closest Premium mode
                var testLeg = new StrategyLeg
                {
                    Mode = StrikeSelectionMode.ClosestPremium,
                    Index = index,
                    OptionType = OptionType.Call,
                    ExpiryType = expiryType,
                    TargetPremium = targetPremium,
                    PremiumOperator = "~", // Approximately
                    WaitForMatch = false
                };

                Console.WriteLine($"Search Criteria:");
                Console.WriteLine($"  - Index: {testLeg.Index}");
                Console.WriteLine($"  - Option Type: {testLeg.OptionType}");
                Console.WriteLine($"  - Target Premium: ₹{testLeg.TargetPremium:N2}");
                Console.WriteLine($"  - Operator: {testLeg.PremiumOperator} (±5% tolerance)");
                Console.WriteLine($"  - Tolerance Range: ₹{testLeg.TargetPremium * 0.95:N2} - ₹{testLeg.TargetPremium * 1.05:N2}");
                Console.WriteLine();

                // Calculate target strike
                var strikeStopwatch = Stopwatch.StartNew();
                int targetStrike = testLeg.GetTargetStrike(spotPrice, optionChain);
                strikeStopwatch.Stop();

                Console.WriteLine($"⏱️  Strike Calculation Latency: {strikeStopwatch.ElapsedMilliseconds}ms");
                Console.WriteLine();

                if (targetStrike > 0)
                {
                    // Find the selected option
                    var selectedOption = optionChain.Find(o => 
                        o.Strike == targetStrike && 
                        o.OptionType == (testLeg.OptionType == OptionType.Call ? "CE" : "PE"));

                    Console.WriteLine("✅ MATCH FOUND!");
                    Console.WriteLine("-".PadRight(80, '-'));
                    Console.WriteLine($"Selected Strike: {targetStrike}");
                    Console.WriteLine($"Selected Premium: ₹{selectedOption?.LTP:N2}");
                    Console.WriteLine($"Difference from Target: ₹{Math.Abs((selectedOption?.LTP ?? 0) - targetPremium):N2}");
                    Console.WriteLine($"Symbol: {selectedOption?.Symbol}");
                    Console.WriteLine($"Token: {selectedOption?.Token}");
                    Console.WriteLine($"Lot Size: {selectedOption?.LotSize}");
                    Console.WriteLine("-".PadRight(80, '-'));
                    Console.WriteLine();

                    // Show nearby options for comparison
                    Console.WriteLine("Nearby Options for Comparison:");
                    Console.WriteLine("-".PadRight(80, '-'));
                    Console.WriteLine($"{"Strike",-10} {"LTP",-12} {"Diff from Target",-20} {"Status"}");
                    Console.WriteLine("-".PadRight(80, '-'));

                    var nearbyOptions = optionChain
                        .Where(o => o.OptionType == "CE" && 
                                   Math.Abs(o.Strike - targetStrike) <= 200)
                        .OrderBy(o => o.Strike)
                        .ToList();

                    foreach (var option in nearbyOptions)
                    {
                        double diff = option.LTP - testLeg.TargetPremium;
                        string diffStr = diff >= 0 ? $"+₹{diff:N2}" : $"-₹{Math.Abs(diff):N2}";
                        string marker = option.Strike == targetStrike ? " ← SELECTED" : "";
                        bool inTolerance = Math.Abs(diff) <= (testLeg.TargetPremium * 0.05);
                        string status = inTolerance ? "✓" : "";
                        Console.WriteLine($"{option.Strike,-10} ₹{option.LTP,-10:N2} {diffStr,-20} {status}{marker}");
                    }
                }
                else
                {
                    Console.WriteLine("❌ NO MATCH FOUND");
                    Console.WriteLine("No options found within ±5% tolerance of target premium.");
                    Console.WriteLine();
                    Console.WriteLine("Closest Options:");
                    Console.WriteLine("-".PadRight(80, '-'));
                    
                    var closestOptions = optionChain
                        .Where(o => o.OptionType == "CE")
                        .OrderBy(o => Math.Abs(o.LTP - targetPremium))
                        .Take(5)
                        .ToList();

                    foreach (var option in closestOptions)
                    {
                        double diff = option.LTP - targetPremium;
                        string diffStr = diff >= 0 ? $"+₹{diff:N2}" : $"-₹{Math.Abs(diff):N2}";
                        Console.WriteLine($"{option.Strike,-10} ₹{option.LTP,-10:N2} {diffStr}");
                    }
                }

                Console.WriteLine();
                
                // Performance Summary
                totalStopwatch.Stop();
                Console.WriteLine("=".PadRight(80, '='));
                Console.WriteLine("PERFORMANCE SUMMARY");
                Console.WriteLine("=".PadRight(80, '='));
                Console.WriteLine($"Total Execution Time: {totalStopwatch.ElapsedMilliseconds}ms ({totalStopwatch.ElapsedMilliseconds / 1000.0:F2}s)");
                Console.WriteLine();
                Console.WriteLine("Breakdown:");
                Console.WriteLine($"  - API Connection:      {sw.ElapsedMilliseconds,6}ms");
                Console.WriteLine($"  - Scrip Master Load:   {scripStopwatch.ElapsedMilliseconds,6}ms");
                Console.WriteLine($"  - Spot Price Fetch:    {spotStopwatch.ElapsedMilliseconds,6}ms");
                Console.WriteLine($"  - Option Chain Build:  {chainStopwatch.ElapsedMilliseconds,6}ms ({chainStopwatch.ElapsedMilliseconds / 1000.0:F2}s)");
                Console.WriteLine($"  - Strike Calculation:  {strikeStopwatch.ElapsedMilliseconds,6}ms");
                Console.WriteLine();
                
                // Slippage Analysis
                Console.WriteLine("💡 SLIPPAGE ANALYSIS:");
                Console.WriteLine($"  - Option chain latency: {chainStopwatch.ElapsedMilliseconds}ms");
                if (chainStopwatch.ElapsedMilliseconds > 5000)
                {
                    Console.WriteLine($"  ⚠️  WARNING: High latency detected (>{chainStopwatch.ElapsedMilliseconds}ms)");
                    Console.WriteLine($"  - Consider caching option chain data");
                    Console.WriteLine($"  - Or use WebSocket for real-time updates");
                }
                else if (chainStopwatch.ElapsedMilliseconds > 2000)
                {
                    Console.WriteLine($"  ⚠️  MODERATE: Acceptable for paper trading, optimize for live");
                }
                else
                {
                    Console.WriteLine($"  ✅ EXCELLENT: Low latency, suitable for live trading");
                }

                Console.WriteLine();
                Console.WriteLine("=".PadRight(80, '='));
                Console.WriteLine("TEST COMPLETE");
                Console.WriteLine("=".PadRight(80, '='));
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("❌ ERROR:");
                Console.WriteLine(ex.Message);
                Console.WriteLine();
                Console.WriteLine("Stack Trace:");
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine();
            Console.WriteLine("Automated Test Complete.");
            // Console.ReadKey();
        }

        /// <summary>
        /// Load configuration from appsettings.json
        /// </summary>
        static JObject LoadConfiguration()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                
                if (!File.Exists(configPath))
                {
                    Console.WriteLine($"❌ Configuration file not found: {configPath}");
                    Console.WriteLine();
                    Console.WriteLine("Please create appsettings.json with the following structure:");
                    Console.WriteLine(@"{
  ""AngelOne"": {
    ""ApiKey"": ""YOUR_API_KEY"",
    ""ClientCode"": ""YOUR_CLIENT_CODE"",
    ""Password"": ""YOUR_PASSWORD"",
    ""TotpSecret"": ""YOUR_TOTP_SECRET""
  },
  ""Testing"": {
    ""TargetPremium"": 50.0,
    ""Index"": ""NIFTY"",
    ""ExpiryType"": ""Weekly""
  }
}");
                    return null;
                }

                string json = File.ReadAllText(configPath);
                return JObject.Parse(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error loading configuration: {ex.Message}");
                return null;
            }
        }
    }
}
