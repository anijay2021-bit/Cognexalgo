using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cognexalgo.Core.Models;
using System.Diagnostics;
using Skender.Stock.Indicators;
using Newtonsoft.Json.Linq;

namespace Cognexalgo.Core.Services
{
    public class AngelOneDataService
    {
        private readonly SmartApiClient _api;
        private readonly TokenService _tokenService;
        private readonly FileLoggingService _logger;
        private readonly ApiRateLimiter _rateLimiter;
        private readonly Dictionary<string, List<Skender.Stock.Indicators.Quote>> _indexHistory = new Dictionary<string, List<Skender.Stock.Indicators.Quote>>();

        public AngelOneDataService(
            SmartApiClient api, 
            TokenService tokenService,
            FileLoggingService logger = null)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
            _logger = logger;
            _rateLimiter = new ApiRateLimiter(maxRequestsPerSecond: 3);
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
                     _logger?.Log("DataService", $"[MOCK] Fetching spot price for {index}");
                     return GenerateMockSpotPrice(index);
                }

                if (string.IsNullOrEmpty(index))
                {
                    _logger?.Log("DataService", "ERROR: GetSpotPriceAsync called with null or empty index name.");
                    return 0;
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

                double spotPrice = 0;

                if (ltpData?.Data?.Ltp != null)
                {
                    spotPrice = ltpData.Data.Ltp;
                }
                else 
                {
                    // FALLBACK: Use Batch/Full Market Data call if LTP solo call fails
                    var batchData = await _api.GetMarketDataBatchAsync("NSE", new List<string> { token });
                    if (batchData != null && batchData.ContainsKey(token))
                    {
                        spotPrice = batchData[token];
                        _logger?.Log("DataService", $"✓ [Fallback] Spot price for {index} fetched via Batch API: ₹{spotPrice:N2}");
                    }
                    else 
                    {
                        throw new Exception($"Failed to fetch LTP for {index} after fallback. API returned null/empty.");
                    }
                }

                _logger?.Log("DataService", $"Spot price for {index}: ₹{spotPrice:N2}");
                return spotPrice;
            }
            catch (Exception ex)
            {
                _logger?.Log("DataService", $"ERROR: Fetching spot price for {index}: {ex.Message}", "ERROR");
                throw new Exception($"Failed to get spot price for {index}: {ex.Message}", ex);
            }
        }

        #endregion

        #region Historical Data

        public async Task PreFetchGlobalHistoryAsync()
        {
            _logger?.Log("DataService", "Pre-fetching global history for Indices...");
            string[] indices = { "NIFTY", "BANKNIFTY", "FINNIFTY" };
            
            foreach (var index in indices)
            {
                try
                {
                    // Fetch 7 days instead of 2 to ensure we have enough data for 200 EMA, even over weekends
                    var history = await GetHistoryAsync(index, "ONE_MINUTE", 7);
                    if (history != null && history.Any())
                    {
                        _indexHistory[index.ToUpper()] = history;
                        _logger?.Log("DataService", $"✓ Global cache populated for {index}");
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Log("DataService", $"ERROR: Pre-fetching {index} failed: {ex.Message}");
                }
            }
        }

        public async Task<List<Skender.Stock.Indicators.Quote>> GetHistoryAsync(string index, string interval = "ONE_MINUTE", int days = 1)
        {
            try
            {
                index = index.ToUpper();

                // Check Cache first if it's a standard index request
                if (interval == "ONE_MINUTE" && days <= 2 && _indexHistory.ContainsKey(index))
                {
                    _logger?.Log("DataService", $"Using cached history for {index}");
                    return _indexHistory[index].ToList(); // Return copy
                }

                // Map index to token
                (string token, string symbol) = index.ToUpper() switch
                {
                    "NIFTY" => ("99926000", "Nifty 50"),
                    "BANKNIFTY" => ("99926009", "Nifty Bank"),
                    "FINNIFTY" => ("99926037", "Nifty Fin Service"),
                    _ => throw new ArgumentException($"Unsupported index: {index}")
                };

                DateTime toDate = DateTime.Now;
                DateTime fromDate = toDate.AddDays(-days);

                _logger?.Log("DataService", $"Fetching History for {index} from {fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}");

                // MOCK DATA CHECK
                if (_api.JwtToken == null)
                {
                     return GenerateMockHistory(index, interval, days);
                }

                await _rateLimiter.WaitAsync();
                var data = await _api.GetHistoricalDataAsync("NSE", token, interval, fromDate, toDate);

                var quotes = new List<Skender.Stock.Indicators.Quote>();
                if (data != null)
                {
                    foreach (var item in data)
                    {
                        var candle = item as JArray;
                        if (candle != null && candle.Count >= 6)
                        {
                            quotes.Add(new Skender.Stock.Indicators.Quote
                            {
                                Date = DateTime.Parse(candle[0].ToString()),
                                Open = candle[1].Value<decimal>(),
                                High = candle[2].Value<decimal>(),
                                Low = candle[3].Value<decimal>(),
                                Close = candle[4].Value<decimal>(),
                                Volume = candle[5].Value<decimal>()
                            });
                        }
                    }
                }

                _logger?.Log("DataService", $"✓ History loaded: {quotes.Count} candles for {index}");
                return quotes;
            }
            catch (Exception ex)
            {
                _logger?.Log("DataService", $"ERROR: Fetching history for {index}: {ex.Message}");
                return new List<Skender.Stock.Indicators.Quote>();
            }
        }

        private List<Skender.Stock.Indicators.Quote> GenerateMockHistory(string index, string interval, int days)
        {
            var list = new List<Skender.Stock.Indicators.Quote>();
            double basePrice = GenerateMockSpotPrice(index);
            DateTime start = DateTime.Now.AddDays(-days);
            
            var random = new Random();

            for (int i = 0; i < 250; i++)
            {
                double change = basePrice * 0.001 * (random.NextDouble() * 2 - 1);
                double close = basePrice + change;
                
                list.Add(new Skender.Stock.Indicators.Quote
                {
                    Date = start.AddMinutes(i),
                    Open = (decimal)basePrice,
                    High = (decimal)Math.Max(basePrice, close) + 2,
                    Low = (decimal)Math.Min(basePrice, close) - 2,
                    Close = (decimal)close,
                    Volume = 1000
                });
                basePrice = close;
            }
            return list;
        }

        #endregion

        #region Option Chain Building

        public async Task<List<OptionChainItem>> BuildOptionChainAsync(string index, string expiryType)
        {
            try
            {
                _logger?.Log("DataService", $"Building option chain for {index} {expiryType} expiry");

                // [UPDATED] Use authoritative expiry from TokenService (Scrip Master)
                DateTime expiryDate = _tokenService.GetNextExpiry(index, expiryType);
                
                // CRITICAL: Scrip Master uses 2-digit year format (e.g., "13FEB26" not "13FEB2026")
                string expiryStr = expiryDate.ToString("ddMMMyy").ToUpper(); 
                
                _logger?.Log("DataService", $"Authoritative Expiry Date: {expiryDate:dd-MMM-yyyy} ({expiryType})");
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