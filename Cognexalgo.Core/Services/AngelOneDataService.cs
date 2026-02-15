using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cognexalgo.Core.Models;
using System.Diagnostics;

namespace Cognexalgo.Core.Services
{
    public class AngelOneDataService
    {
        private readonly SmartApiClient _api;
        private readonly TokenService _tokenService;
        private readonly FileLoggingService _logger;
        private readonly ApiRateLimiter _rateLimiter;

        public AngelOneDataService(
            SmartApiClient api, 
            TokenService tokenService,
            FileLoggingService logger = null)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
            _logger = logger;
            _rateLimiter = new ApiRateLimiter(maxRequestsPerSecond: 10);
        }

        #region Spot Price Fetching

        /// <summary>
        /// Fetches real-time spot price (LTP) for an index using hardcoded NSE tokens
        /// </summary>
        public async Task<double> GetSpotPriceAsync(string index)
        {
            try
            {
                // MOCK DATA CHECK
                if (_api.JwtToken == null)
                {
                     return GenerateMockSpotPrice(index);
                }

                // Map index name to hardcoded Angel One NSE Index Tokens
                (string token, string symbol) = index.ToUpper() switch
                {
                    "NIFTY" => ("99926000", "Nifty 50"),
                    "BANKNIFTY" => ("99926009", "Nifty Bank"),
                    "FINNIFTY" => ("99926037", "Nifty Fin Service"),
                    _ => throw new ArgumentException($"Unsupported index: {index}")
                };

                _logger?.Log("DataService", $"Fetching spot price for {index} using Token: {token}");
                Console.WriteLine($"DEBUG: DataService Fetching {index} ({token})");

                // Apply rate limiting
                await _rateLimiter.WaitAsync();

                // Fetch LTP from Angel One using the 'NSE' exchange for indices
                var ltpData = await _api.GetLTPDataAsync(
                    exchange: "NSE",
                    tradingSymbol: symbol,
                    symbolToken: token
                );

                if (ltpData?.Data?.Ltp == null)
                {
                    throw new Exception($"Failed to fetch LTP for {index}. API returned null data.");
                }

                double spotPrice = ltpData.Data.Ltp;
                _logger?.Log("DataService", $"Spot price for {index}: ₹{spotPrice:N2}");

                return spotPrice;
            }
            catch (Exception ex)
            {
                _logger?.Log("DataService", $"ERROR: Fetching spot price for {index}: {ex.Message}");
                throw new Exception($"Failed to get spot price for {index}: {ex.Message}", ex);
            }
        }

        #endregion

        #region Option Chain Building

        public async Task<List<OptionChainItem>> BuildOptionChainAsync(string index, string expiryType)
        {
            try
            {
                _logger?.Log("DataService", $"Building option chain for {index} {expiryType} expiry");

                DateTime expiryDate = GetExpiryDate(index, expiryType);
                // CRITICAL: Scrip Master uses 2-digit year format (e.g., "13FEB26" not "13FEB2026")
                string expiryStr = expiryDate.ToString("ddMMMyy").ToUpper(); 
                
                _logger?.Log("DataService", $"Calculated expiry date: {expiryDate:dd-MMM-yyyy}");
                _logger?.Log("DataService", $"Expiry string format (2-digit year): {expiryStr}");

                // Use index directly (NIFTY or BANKNIFTY) - NFO segment for derivatives
                // DO NOT use "Nifty 50" or "Nifty Bank" - those are for NSE cash segment
                _logger?.Log("DataService", $"Searching for {index} options with expiry {expiryStr} in NFO segment");
                
                // MOCK DATA CHECK
                if (_api.JwtToken == null)
                {
                     return GenerateMockOptionChain(index);
                }

                var instruments = _tokenService.GetInstrumentsByExpiry(index, expiryStr);

                if (instruments == null || !instruments.Any())
                {
                    _logger?.Log("DataService", $"ERROR: No options found for {index} expiry {expiryStr}");
                    return new List<OptionChainItem>();
                }

                _logger?.Log("DataService", $"Found {instruments.Count} total instruments for {index} {expiryStr}");

                // OPTIMIZATION 1: Get spot price to calculate ATM
                double spotPrice = await GetSpotPriceAsync(index);
                _logger?.Log("DataService", $"Spot price for ATM calculation: ₹{spotPrice:N2}");

                // OPTIMIZATION 2: Filter to ATM ± 10 strikes (80% reduction)
                var strikes = instruments.Select(i => i.Strike).Distinct().OrderBy(s => s).ToList();
                int atmStrike = strikes.OrderBy(s => Math.Abs(s - spotPrice)).First();
                int atmIndex = strikes.IndexOf(atmStrike);
                
                int startIndex = Math.Max(0, atmIndex - 10);
                int endIndex = Math.Min(strikes.Count - 1, atmIndex + 10);
                var filteredStrikes = strikes.Skip(startIndex).Take(endIndex - startIndex + 1).ToHashSet();

                var filteredInstruments = instruments.Where(i => filteredStrikes.Contains(i.Strike)).ToList();
                
                _logger?.Log("DataService", $"ATM Strike: {atmStrike}");
                _logger?.Log("DataService", $"Filtered to ATM ± 10 strikes: {filteredInstruments.Count} instruments (was {instruments.Count})");
                _logger?.Log("DataService", $"Reduction: {100 - (filteredInstruments.Count * 100 / instruments.Count)}%");

                // OPTIMIZATION 3: Batch LTP fetching (50 tokens per request)
                var optionChain = new List<OptionChainItem>();
                var tokenList = filteredInstruments.Select(i => i.Token).ToList();
                var batches = tokenList.Chunk(50).ToList();

                _logger?.Log("DataService", $"Fetching LTPs in {batches.Count} batch(es) of up to 50 tokens each");

                int batchNumber = 1;
                foreach (var batch in batches)
                {
                    await _rateLimiter.WaitAsync();

                    var batchTokens = batch.ToList();
                    var ltpMap = await _api.GetMarketDataBatchAsync("NFO", batchTokens);

                    _logger?.Log("DataService", $"Batch {batchNumber}/{batches.Count}: Fetched {ltpMap.Count} LTPs");

                    // Map LTPs back to instruments
                    foreach (var inst in filteredInstruments.Where(i => batchTokens.Contains(i.Token)))
                    {
                        if (ltpMap.TryGetValue(inst.Token, out double ltp))
                        {
                            optionChain.Add(new OptionChainItem
                            {
                                Strike = inst.Strike,
                                OptionType = inst.OptionType,
                                LTP = ltp,
                                Symbol = inst.Symbol,
                                Token = inst.Token,
                                LotSize = inst.LotSize
                            });
                        }
                        else
                        {
                            _logger?.Log("DataService", $"WARNING: No LTP data for {inst.Symbol}");
                        }
                    }

                    batchNumber++;
                }

                _logger?.Log("DataService", $"✓ Option chain built: {optionChain.Count} options with LTP data");
                return optionChain;
            }
            catch (Exception ex)
            {
                _logger?.Log("DataService", $"ERROR: Building option chain: {ex.Message}");
                // Fallback to Mock if API fails completely
                if (_api.JwtToken == null) return GenerateMockOptionChain(index);
                throw new Exception($"Failed to build option chain for {index}: {ex.Message}", ex);
            }
        }

        #endregion

        #region Expiry Date Calculation

        public DateTime GetExpiryDate(string index, string expiryType)
        {
            DateTime today = DateTime.Today;

            if (expiryType.Equals("Weekly", StringComparison.OrdinalIgnoreCase))
            {
                // NIFTY weekly expiries are on TUESDAY (changed from Thursday in 2024)
                int daysUntilTuesday = ((int)DayOfWeek.Tuesday - (int)today.DayOfWeek + 7) % 7;
                
                // If today is Tuesday and market has closed (3:30 PM), move to next Tuesday
                if (daysUntilTuesday == 0 && DateTime.Now.Hour >= 15 && DateTime.Now.Minute >= 30)
                {
                    daysUntilTuesday = 7;
                }
                
                DateTime expiryDate = today.AddDays(daysUntilTuesday);
                _logger?.Log("DataService", $"Weekly expiry calculated: {expiryDate:dd-MMM-yyyy} (Tuesday)");
                return expiryDate;
            }
            else 
            {
                // Monthly expiry: Last Tuesday of the month
                DateTime lastDayOfMonth = new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
                DateTime lastTuesday = lastDayOfMonth;
                while (lastTuesday.DayOfWeek != DayOfWeek.Tuesday) 
                    lastTuesday = lastTuesday.AddDays(-1);

                // If we've passed this month's expiry, calculate next month's
                if (today > lastTuesday || (today == lastTuesday && DateTime.Now.Hour >= 15 && DateTime.Now.Minute >= 30))
                {
                    DateTime nextMonth = today.AddMonths(1);
                    lastDayOfMonth = new DateTime(nextMonth.Year, nextMonth.Month, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month));
                    lastTuesday = lastDayOfMonth;
                    while (lastTuesday.DayOfWeek != DayOfWeek.Tuesday) 
                        lastTuesday = lastTuesday.AddDays(-1);
                }
                
                _logger?.Log("DataService", $"Monthly expiry calculated: {lastTuesday:dd-MMM-yyyy} (Last Tuesday)");
                return lastTuesday;
            }
        }

        #endregion

        #region Mock Data Generation

        private double GenerateMockSpotPrice(string index)
        {
            var random = new Random();
            double basePrice = index.ToUpper() switch
            {
                "NIFTY" => 22000,
                "BANKNIFTY" => 47000,
                "FINNIFTY" => 20500,
                _ => 10000
            };

            // Add +/- 0.5% fluctuation
            double fluctuation = basePrice * 0.005 * (random.NextDouble() * 2 - 1);
            return Math.Round(basePrice + fluctuation, 2);
        }

        private List<OptionChainItem> GenerateMockOptionChain(string index)
        {
            var list = new List<OptionChainItem>();
            double spot = GenerateMockSpotPrice(index);
            int step = index == "NIFTY" ? 50 : 100;
            int startStrike = ((int)spot / step) * step - (step * 10);

            var random = new Random();

            for (int i = 0; i < 20; i++)
            {
                int strike = startStrike + (i * step);
                
                // Intrinsic Value
                double ceIntrinsic = Math.Max(0, spot - strike);
                double peIntrinsic = Math.Max(0, strike - spot);

                // Time Value (Random for mock)
                double timeValue = 50 + (random.NextDouble() * 20);

                list.Add(new OptionChainItem { Symbol = $"{index} {strike} CE", Token = $"MOCK_CE_{strike}", Strike = strike, OptionType = "CE", LTP = Math.Round(ceIntrinsic + timeValue, 2), LotSize = 50 });
                list.Add(new OptionChainItem { Symbol = $"{index} {strike} PE", Token = $"MOCK_PE_{strike}", Strike = strike, OptionType = "PE", LTP = Math.Round(peIntrinsic + timeValue, 2), LotSize = 50 });
            }

            return list;
        }

        #endregion

        #region Helper Methods

        public bool IsMarketOpen()
        {
            // Always return true for Mock/Paper Trading
            return true;
        }

        #endregion
    }
}