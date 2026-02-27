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
        private readonly HistoryCacheService _cacheService;
        // Multi-timeframe cache: key = "NIFTY|ONE_MINUTE", "NIFTY|FIVE_MINUTE", etc.
        private readonly Dictionary<string, List<Skender.Stock.Indicators.Quote>> _indexHistory = new Dictionary<string, List<Skender.Stock.Indicators.Quote>>();

        /// <summary>
        /// Progress callback for pre-login UI: (statusMessage, progressPercent)
        /// </summary>
        public event Action<string, int>? OnDownloadProgress;

        /// <summary>
        /// Angel One interval spec: (apiIntervalName, maxDaysAllowed, displayName)
        /// </summary>
        private static readonly (string Interval, int MaxDays, string Display)[] DeepIntervals = new[]
        {
            ("ONE_MINUTE",    30,  "1m"),
            ("THREE_MINUTE",  60,  "3m"),
            ("FIVE_MINUTE",   100, "5m"),
            ("TEN_MINUTE",    100, "10m"),
            ("FIFTEEN_MINUTE",200, "15m"),
            ("THIRTY_MINUTE", 200, "30m"),
            ("ONE_HOUR",      400, "1h"),
            // Daily: Angel One supports ONE_DAY up to ~2 years
            ("ONE_DAY",       730, "1D"),
        };

        public AngelOneDataService(
            SmartApiClient api, 
            TokenService tokenService,
            FileLoggingService logger = null)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
            _logger = logger;
            _rateLimiter = new ApiRateLimiter(maxRequestsPerSecond: 3);
            _cacheService = new HistoryCacheService();
        }

        #region Spot Price Fetching

        /// <summary>
        /// Fetches real-time spot price (LTP) for an index using hardcoded NSE tokens
        /// </summary>
        public async Task<double> GetSpotPriceAsync(string index)
        {
            try
            {
                // NO MOCK FALLBACK - Return 0 if not logged in
                if (_api.JwtToken == null)
                {
                     _logger?.Log("DataService", $"WARNING: No JWT token. Cannot fetch live spot price for {index}. Providing 0.");
                     return 0;
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
                    "MIDCPNIFTY" => ("99926030", "NIFTY MID SELECT"),
                    "SENSEX" => ("99919017", "SENSEX"),
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

        /// <summary>
        /// [DEEP PRE-LOGIN PROTOCOL] Downloads historical data for ALL intervals 
        /// for major indices. This ensures strategies on any timeframe start warm.
        /// 
        /// Downloads: 1m(30d), 3m(60d), 5m(100d), 10m(100d), 15m(200d), 30m(200d), 1h(400d)
        /// For: NIFTY, BANKNIFTY, FINNIFTY
        /// Total calls: 3 indices × 7 intervals = 21 API calls
        /// </summary>
        public async Task PreFetchDeepHistoryAsync()
        {
            string[] indices = { "NIFTY", "BANKNIFTY", "FINNIFTY" };
            int totalSteps = indices.Length * DeepIntervals.Length;
            int currentStep = 0;

            _logger?.Log("DataService", $"═══ DEEP HISTORY DOWNLOAD: {totalSteps} calls ({indices.Length} indices × {DeepIntervals.Length} intervals) ═══");

            foreach (var index in indices)
            {
                foreach (var (interval, maxDays, display) in DeepIntervals)
                {
                    currentStep++;
                    int progressPercent = (int)((double)currentStep / totalSteps * 100);

                    try
                    {
                        string statusMsg = $"📥 {index} {display} ({currentStep}/{totalSteps})";
                        OnDownloadProgress?.Invoke(statusMsg, progressPercent);

                        var history = await GetHistoryAsync(index, interval, maxDays);
                        string cacheKey = $"{index.ToUpper()}|{interval}";

                        if (history != null && history.Any())
                        {
                            _indexHistory[cacheKey] = history;
                            
                            // [NEW] Persist to SQLite
                            await _cacheService.SaveHistoryAsync(index, interval, history);
                            
                            _logger?.Log("DataService", $"  ✓ {index} {display}: {history.Count} candles ({maxDays}d) [SAVED TO CACHE]");
                        }
                        else
                        {
                            _logger?.Log("DataService", $"  ⚠ {index} {display}: 0 candles returned");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Log("DataService", $"  ✗ {index} {display}: {ex.Message}");
                    }
                }
            }

            // ── Derive Weekly + Monthly from Daily (Angel One has no W/M API) ───
            OnDownloadProgress?.Invoke("📊 Aggregating Weekly & Monthly candles...", 98);
            foreach (var index in indices)
            {
                string dailyKey = $"{index.ToUpper()}|ONE_DAY";
                if (_indexHistory.TryGetValue(dailyKey, out var dailyCandles) && dailyCandles.Any())
                {
                    var weekly  = AggregatePeriod(dailyCandles, q => GetWeekStart(q.Date));
                    var monthly = AggregatePeriod(dailyCandles, q => new DateTime(q.Date.Year, q.Date.Month, 1));

                    if (weekly.Any())
                    {
                        _indexHistory[$"{index.ToUpper()}|ONE_WEEK"]  = weekly;
                        await _cacheService.SaveHistoryAsync(index, "ONE_WEEK",  weekly);
                    }
                    if (monthly.Any())
                    {
                        _indexHistory[$"{index.ToUpper()}|ONE_MONTH"] = monthly;
                        await _cacheService.SaveHistoryAsync(index, "ONE_MONTH", monthly);
                    }
                    _logger?.Log("DataService", $"  ✓ {index}: {weekly.Count} weekly, {monthly.Count} monthly candles derived");
                }
            }

            int totalCandles = _indexHistory.Values.Sum(v => v.Count);
            _logger?.Log("DataService", $"═══ DEEP HISTORY COMPLETE: {_indexHistory.Count} buffers, {totalCandles:N0} total candles ═══");
            OnDownloadProgress?.Invoke($"✅ Data Ready — {totalCandles:N0} candles across all timeframes", 100);
        }

        /// <summary>
        /// Legacy shallow prefetch — now calls deep version.
        /// </summary>
        public async Task PreFetchGlobalHistoryAsync()
        {
            await PreFetchDeepHistoryAsync();
        }

        /// <summary>
        /// Get cached history for a specific index and interval.
        /// </summary>
        public List<Skender.Stock.Indicators.Quote>? GetCachedHistory(string index, string interval)
        {
            string key = $"{index.ToUpper()}|{interval}";
            return _indexHistory.ContainsKey(key) ? _indexHistory[key].ToList() : null;
        }

        /// <summary>
        /// Get total cached candle count across all buffers.
        /// </summary>
        public int GetTotalCachedCandles()
        {
            return _indexHistory.Values.Sum(v => v.Count);
        }

        public async Task<List<Skender.Stock.Indicators.Quote>> GetHistoryAsync(string index, string interval = "ONE_MINUTE", int days = 1)
        {
            try
            {
                index = index.ToUpper();

                // Priority 1: In-memory cache (fastest)
                string cacheKey = $"{index}|{interval}";
                if (_indexHistory.ContainsKey(cacheKey))
                {
                    _logger?.Log("DataService", $"Using in-memory history for {index} [{interval}]");
                    return _indexHistory[cacheKey].ToList();
                }

                // Priority 2: SQLite Local Cache (Persistence)
                var localCached = await _cacheService.GetHistoryAsync(index, interval, days);
                if (localCached != null && localCached.Count > 50) // Only use if we have significant history
                {
                    _logger?.Log("DataService", $"Using SQLite local cache for {index} [{interval}] ({localCached.Count} candles)");
                    _indexHistory[cacheKey] = localCached; // Optmize for next call
                    return localCached;
                }

                // Legacy single-key cache fallback
                if (interval == "ONE_MINUTE" && _indexHistory.ContainsKey(index))
                {
                    _logger?.Log("DataService", $"Using legacy cached history for {index}");
                    return _indexHistory[index].ToList();
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

                // MOCK DATA CHECK - REMOVED Mock fallback if JWT is missing
                // Instead of generating mock data, we should attempt to use the local cache 
                // or just return empty to avoid polluting indicators with fake 2 price values.
                if (_api.JwtToken == null)
                {
                    _logger?.Log("DataService", $"WARNING: No JWT token. Cannot fetch live history for {index}. Providing local cache only.");
                    return localCached ?? new List<Quote>();
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
                
                // [NEW] Update local cache with fresh data
                if (quotes.Any())
                {
                    await _cacheService.SaveHistoryAsync(index, interval, quotes);
                }

                return quotes;
            }
            catch (Exception ex)
            {
                _logger?.Log("DataService", $"ERROR: Fetching history for {index}: {ex.Message}");
                return new List<Quote>();
            }
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
                
                // MOCK DATA CHECK - REMOVED
                if (_api.JwtToken == null)
                {
                     _logger?.Log("DataService", "WARNING: No JWT token. Cannot fetch live option chain.");
                     return new List<OptionChainItem>();
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
                                Strike     = inst.Strike,
                                OptionType = inst.OptionType,
                                LTP        = ltp,
                                Symbol     = inst.Symbol,
                                Token      = inst.Token,
                                LotSize    = inst.LotSize,
                                ExpiryDate = expiryDate   // ← enables DaysToExpiry for Greeks calc
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
                // Fallback
                if (_api.JwtToken == null) return new List<OptionChainItem>();
                throw new Exception($"Failed to build option chain for {index}: {ex.Message}", ex);
            }
        }


        #endregion

        #region Helper Methods

        public bool IsMarketOpen()
        {
            // Always return true for Mock/Paper Trading
            return true;
        }

        /// <summary>
        /// Aggregate daily quotes into weekly or monthly bars.
        /// <paramref name="periodStart"/> maps each quote to its period-start date.
        /// </summary>
        private static List<Skender.Stock.Indicators.Quote> AggregatePeriod(
            List<Skender.Stock.Indicators.Quote> daily,
            Func<Skender.Stock.Indicators.Quote, DateTime> periodStart)
        {
            return daily
                .GroupBy(periodStart)
                .OrderBy(g => g.Key)
                .Select(g => new Skender.Stock.Indicators.Quote
                {
                    Date   = g.Key,
                    Open   = g.First().Open,
                    High   = g.Max(q => q.High),
                    Low    = g.Min(q => q.Low),
                    Close  = g.Last().Close,
                    Volume = g.Sum(q => q.Volume)
                })
                .ToList();
        }

        /// <summary>Returns the Monday of the ISO week containing <paramref name="dt"/>.</summary>
        private static DateTime GetWeekStart(DateTime dt)
        {
            int diff = (7 + (int)dt.DayOfWeek - (int)DayOfWeek.Monday) % 7;
            return dt.Date.AddDays(-diff);
        }

        #endregion
    }
}