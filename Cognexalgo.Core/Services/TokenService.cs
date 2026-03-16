using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;

namespace Cognexalgo.Core.Services
{
    public class TokenService
    {
        private ConcurrentDictionary<string, string> _symbolToToken = new ConcurrentDictionary<string, string>();
        private ConcurrentDictionary<string, int> _symbolToLotSize = new ConcurrentDictionary<string, int>();
        // [NEW] Cache for distinct expiry dates per index
        private ConcurrentDictionary<string, HashSet<DateTime>> _indexExpiries = new ConcurrentDictionary<string, HashSet<DateTime>>();
        
        private const string SCRIP_MASTER_URL = "https://margincalculator.angelbroking.com/OpenAPI_File/files/OpenAPIScripMaster.json";
        private bool _hasLoggedSamples = false; // Track if we've logged sample symbols

        private static readonly string ScripMasterPath =
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OpenAPIScripMaster.json");
        private static readonly string ScripMasterDatePath =
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scrip_master_date.txt");

        // ── Staleness helpers ────────────────────────────────────────────────────

        public async Task<bool> IsScripMasterStale()
        {
            if (!System.IO.File.Exists(ScripMasterPath)) return true;
            if (!System.IO.File.Exists(ScripMasterDatePath)) return true;

            string savedDate = (await System.IO.File.ReadAllTextAsync(ScripMasterDatePath)).Trim();
            bool isOldDate = savedDate != DateTime.Today.ToString("yyyy-MM-dd");
            if (isOldDate)
                Console.WriteLine($"[TokenService] Scrip Master is from {savedDate}, today is {DateTime.Today:yyyy-MM-dd} — STALE");
            else
                Console.WriteLine("[TokenService] Scrip Master is fresh (downloaded today)");
            return isOldDate;
        }

        public async Task SaveScripMasterDate()
        {
            await System.IO.File.WriteAllTextAsync(ScripMasterDatePath, DateTime.Today.ToString("yyyy-MM-dd"));
        }

        /// <summary>Force-downloads a fresh Scrip Master, ignoring any local cache.</summary>
        public async Task DownloadAndLoadScripMasterAsync()
        {
            // Remove stale cache so LoadMasterAsync always re-downloads
            if (System.IO.File.Exists(ScripMasterPath))
                System.IO.File.Delete(ScripMasterPath);
            await LoadMasterAsync();
        }

        /// <summary>
        /// Smart load: downloads fresh if the saved date is not today;
        /// otherwise loads from today's on-disk cache (fast path).
        /// </summary>
        public async Task LoadScripMasterSmartAsync()
        {
            bool stale = await IsScripMasterStale();
            if (stale)
            {
                Console.WriteLine("[TokenService] Downloading fresh Scrip Master...");
                await DownloadAndLoadScripMasterAsync();
                await SaveScripMasterDate();
                Console.WriteLine("[TokenService] ✓ Fresh Scrip Master loaded and cached.");
            }
            else
            {
                Console.WriteLine("[TokenService] Loading Scrip Master from today's cache...");
                await LoadMasterAsync();
                Console.WriteLine("[TokenService] ✓ Scrip Master loaded from cache.");
            }
        }

        public async Task LoadMasterAsync()
        {
            await Task.Run(async () => 
            {
                string cachePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OpenAPIScripMaster.json");
                string json = null;

                // Try Load from Cache
                if (System.IO.File.Exists(cachePath))
                {
                    var fileInfo = new System.IO.FileInfo(cachePath);
                    if (fileInfo.LastWriteTime > DateTime.Now.AddDays(-1) && fileInfo.Length > 0)
                    {
                        Console.WriteLine("Loading Scrip Master from Cache...");
                        json = await System.IO.File.ReadAllTextAsync(cachePath);
                    }
                }

                using (var client = new HttpClient())
                {
                    try
                    {
                        if (string.IsNullOrEmpty(json))
                        {
                            Console.WriteLine("Downloading Scrip Master...");
                            var response = await client.GetAsync(SCRIP_MASTER_URL);
                            json = await response.Content.ReadAsStringAsync();
                            
                            Console.WriteLine($"Downloaded {json.Length} bytes of JSON data");
                            
                            // Save to Cache
                            await System.IO.File.WriteAllTextAsync(cachePath, json);
                        }
                        
                        var list = JsonConvert.DeserializeObject<List<ScripItem>>(json);
                        
                        Console.WriteLine($"Deserialized {list?.Count ?? 0} items from Scrip Master");
                        
                        if (list == null || list.Count == 0)
                        {
                            Console.WriteLine("ERROR: Scrip Master list is null or empty!");
                            return;
                        }
                        
                        // Initialize dictionaries with appropriate capacity for performance
                        _symbolToToken = new ConcurrentDictionary<string, string>(Environment.ProcessorCount * 2, list.Count * 2);
                        _symbolToLotSize = new ConcurrentDictionary<string, int>(Environment.ProcessorCount * 2, list.Count);
                        
                        int addedCount = 0;
                        int skippedCount = 0;
                        int nullSymbolCount = 0;
                        int nullTokenCount = 0;
                        var sampleSkipped = new List<string>();
                        
                        foreach (var item in list)
                        {
                            // Map "NIFTY26FEB24500CE" -> "12345"
                            
                            // [NEW] Capture Expiry for Index Options
                            if (!string.IsNullOrEmpty(item.Expiry) && !string.IsNullOrEmpty(item.Symbol))
                            {
                                if (DateTime.TryParseExact(item.Expiry, "ddMMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime expiryDate))
                                {
                                     string indexKey = null;
                                     // Categorize by index based on Symbol prefix (Index Options only)
                                     // Optimization: Check for OPTIDX or common index prefixes
                                     if (item.Symbol.StartsWith("NIFTY") && !item.Symbol.StartsWith("NIFTYIT")) indexKey = "NIFTY";
                                     else if (item.Symbol.StartsWith("BANKNIFTY")) indexKey = "BANKNIFTY";
                                     else if (item.Symbol.StartsWith("FINNIFTY")) indexKey = "FINNIFTY";
                                     
                                     if (indexKey != null)
                                     {
                                         _indexExpiries.AddOrUpdate(indexKey, 
                                             new HashSet<DateTime> { expiryDate }, 
                                             (key, existingSet) => { existingSet.Add(expiryDate); return existingSet; });
                                     }
                                }
                            }

                            // Key format: SYMBOL (e.g. NIFTY26FEB..., BANKNIFTY...)
                            if (!string.IsNullOrEmpty(item.Symbol) && !string.IsNullOrEmpty(item.Token))
                            {
                                _symbolToToken[item.Symbol] = item.Token;
                                if (!string.IsNullOrEmpty(item.Name))
                                {
                                    _symbolToToken[item.Name] = item.Token; // Map "NIFTY" -> Token
                                }
                                
                                // Store lot size
                                if (int.TryParse(item.LotSize, out int lotSize) && lotSize > 0)
                                {
                                    _symbolToLotSize[item.Symbol] = lotSize;
                                    if (!string.IsNullOrEmpty(item.Name))
                                    {
                                        _symbolToLotSize[item.Name] = lotSize;
                                    }
                                }
                                addedCount++;
                            }
                            else
                            {
                                skippedCount++;
                                
                                // Track skip reasons
                                if (string.IsNullOrEmpty(item.Symbol))
                                    nullSymbolCount++;
                                if (string.IsNullOrEmpty(item.Token))
                                    nullTokenCount++;
                                
                                // Collect sample skipped items (first 5)
                                if (sampleSkipped.Count < 5)
                                {
                                    sampleSkipped.Add($"Symbol: '{item.Symbol ?? "NULL"}', Token: '{item.Token ?? "NULL"}', Name: '{item.Name ?? "NULL"}'");
                                }
                            }
                        }
                        
                        Console.WriteLine($"Processing complete: {addedCount} added, {skippedCount} skipped");
                        Console.WriteLine($"Skip reasons: {nullSymbolCount} null symbols, {nullTokenCount} null tokens");
                        
                        if (sampleSkipped.Count > 0)
                        {
                            Console.WriteLine("Sample skipped items:");
                            foreach (var sample in sampleSkipped)
                                Console.WriteLine($"  {sample}");
                        }
                        
                        Console.WriteLine($"Scrip Master Loaded. Dictionary Count: {_symbolToToken.Count}");
                        
                        // Log sample symbols for debugging
                        Console.WriteLine("=== SCRIP MASTER LOADED ===");
                        var niftySamples = _symbolToToken.Keys
                            .Where(k => k.Contains("NIFTY") && (k.Contains("CE") || k.Contains("PE")))
                            .Take(5)
                            .ToList();
                        Console.WriteLine($"Sample NIFTY option symbols ({niftySamples.Count}):");
                        foreach (var s in niftySamples)
                            Console.WriteLine($"  {s}");
                        
                        var niftyIndex = _symbolToToken.Keys
                            .Where(k => k.Equals("NIFTY", StringComparison.OrdinalIgnoreCase) || k.Equals("Nifty 50", StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        Console.WriteLine($"NIFTY index symbols ({niftyIndex.Count}):");
                        foreach (var s in niftyIndex)
                            Console.WriteLine($"  {s}");
                        Console.WriteLine("=== END SAMPLE ===");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"ERROR loading Scrip Master: {ex.Message}");
                        Console.WriteLine($"Stack trace: {ex.StackTrace}");
                    }
                }
            });
        }

        /// <summary>
        /// Get the total number of symbols loaded in the dictionary
        /// </summary>
        public int GetSymbolCount()
        {
            return _symbolToToken.Count;
        }

        public string GetToken(string symbol)
        {
            // Try Exact Match
            if (_symbolToToken.TryGetValue(symbol, out var token))
                return token;
                
            // Handle "-EQ" suffix difference if any
            if (_symbolToToken.TryGetValue(symbol + "-EQ", out token))
                return token;

            return null;
        }

        public int GetLotSize(string symbol)
        {
            // Try Exact Match
            if (_symbolToLotSize.TryGetValue(symbol, out var lotSize))
                return lotSize;
                
            // Handle "-EQ" suffix difference if any
            if (_symbolToLotSize.TryGetValue(symbol + "-EQ", out lotSize))
                return lotSize;

            return 1; // Default to 1 if not found
        }

        public (string token, int lotSize) GetInstrumentInfo(string symbol)
        {
            var token = GetToken(symbol);
            var lotSize = GetLotSize(symbol);
            return (token, lotSize);
        }

        /// <summary>
        /// Get instruments by index and expiry date for option chain building
        /// </summary>
        public List<TokenInstrumentInfo> GetInstrumentsByExpiry(string index, string expiryStr)
        {
            var instruments = new List<TokenInstrumentInfo>();

            // Log sample symbols on first call
            if (!_hasLoggedSamples)
            {
                _hasLoggedSamples = true;
                Console.WriteLine("=== SCRIP MASTER SAMPLE SYMBOLS ===");
                
                var niftySamples = _symbolToToken.Keys
                    .Where(k => k.Contains("NIFTY") && k.Contains("CE"))
                    .Take(10)
                    .ToList();
                
                Console.WriteLine($"Sample NIFTY symbols ({niftySamples.Count} shown):");
                foreach (var sample in niftySamples)
                {
                    Console.WriteLine($"  Symbol: '{sample}', Token: '{_symbolToToken[sample]}'");
                }
                
                var bankNiftySamples = _symbolToToken.Keys
                    .Where(k => k.Contains("BANKNIFTY") && k.Contains("CE"))
                    .Take(10)
                    .ToList();
                
                Console.WriteLine($"Sample BANKNIFTY symbols ({bankNiftySamples.Count} shown):");
                foreach (var sample in bankNiftySamples)
                {
                    Console.WriteLine($"  Symbol: '{sample}', Token: '{_symbolToToken[sample]}'");
                }
                Console.WriteLine("=== END SAMPLE SYMBOLS ===");
            }

            Console.WriteLine($"[TokenService] Searching for: index='{index}', expiryStr='{expiryStr}'");
            Console.WriteLine($"[TokenService] Total symbols in dictionary: {_symbolToToken.Count}");
            
            // DIAGNOSTIC DUMP: Show first 5 symbols that contain the expiry string
            Console.WriteLine($"[TokenService] === DIAGNOSTIC DUMP FOR EXPIRY '{expiryStr}' ===");
            var expiryMatches = _symbolToToken.Keys
                .Where(k => k.Contains(expiryStr))
                .Take(5)
                .ToList();
            
            if (expiryMatches.Any())
            {
                Console.WriteLine($"[TokenService] Found {expiryMatches.Count} symbols containing '{expiryStr}' (showing first 5):");
                foreach (var symbol in expiryMatches)
                {
                    Console.WriteLine($"[TokenService]   '{symbol}'");
                }
            }
            else
            {
                Console.WriteLine($"[TokenService] ⚠️ NO symbols found containing '{expiryStr}'");
                Console.WriteLine($"[TokenService] Trying alternative formats...");
                
                // Try different date formats to help diagnose
                var altFormats = new[] { 
                    expiryStr.Substring(0, 7),  // e.g., "13FEB20" instead of "13FEB2026"
                    expiryStr.Substring(0, 5),  // e.g., "13FEB"
                };
                
                foreach (var altFormat in altFormats)
                {
                    var altMatches = _symbolToToken.Keys.Where(k => k.Contains(altFormat)).Take(3).ToList();
                    if (altMatches.Any())
                    {
                        Console.WriteLine($"[TokenService] Found symbols with '{altFormat}': {string.Join(", ", altMatches)}");
                    }
                }
            }
            Console.WriteLine($"[TokenService] === END DIAGNOSTIC DUMP ===");

            foreach (var kvp in _symbolToToken)
            {
                string symbol = kvp.Key;
                
                // Filter by index name and expiry
                if (symbol.StartsWith(index, StringComparison.OrdinalIgnoreCase) && 
                    symbol.Contains(expiryStr))
                {
                    // Parse strike and option type from symbol
                    // Example: "NIFTY10FEB2623100CE" -> Expiry "10FEB26" -> Strike: 23100, Type: CE
                    if (TryParseOptionSymbol(symbol, expiryStr, out int strike, out string optionType))
                    {
                        instruments.Add(new TokenInstrumentInfo
                        {
                            Symbol = symbol,
                            Token = kvp.Value,
                            Strike = strike,
                            OptionType = optionType,
                            LotSize = GetLotSize(symbol)
                        });
                    }
                }
            }

            Console.WriteLine($"[TokenService] Found {instruments.Count} instruments matching criteria");
            if (instruments.Count > 0)
            {
                Console.WriteLine($"[TokenService] Sample results (first 3):");
                foreach (var inst in instruments.Take(3))
                {
                    Console.WriteLine($"  Strike: {inst.Strike}, Type: {inst.OptionType}, Symbol: {inst.Symbol}");
                }
            }
            else
            {
                Console.WriteLine($"[TokenService] ⚠️ NO INSTRUMENTS FOUND!");
                Console.WriteLine($"[TokenService] This means no symbols matched: StartsWith('{index}') AND Contains('{expiryStr}')");
            }

            return instruments.OrderBy(i => i.Strike).ToList();
        }

        /// <summary>
        /// Get instrument by name (for index spot price fetching)
        /// </summary>
        public TokenInstrumentInfo GetInstrumentByName(string name, string exchange)
        {
            if (_symbolToToken.TryGetValue(name, out string token))
            {
                return new TokenInstrumentInfo
                {
                    Symbol = name,
                    Token = token,
                    LotSize = GetLotSize(name)
                };
            }
            return null;
        }

        /// <summary>
        /// Parse option symbol to extract strike and option type
        /// Uses expiry string to correctly identify where the symbol ends and strike begins
        /// </summary>
        private bool TryParseOptionSymbol(string symbol, string expiryStr, out int strike, out string optionType)
        {
            strike = 0;
            optionType = null;

            try
            {
                // Symbol format: NIFTY10FEB2623100CE
                // expiryStr: 10FEB26
                
                if (symbol.Length < 2)
                    return false;

                // Extract Option Type (last 2 chars)
                optionType = symbol.Substring(symbol.Length - 2);
                if (optionType != "CE" && optionType != "PE")
                    return false;

                // Validate expiry exists in symbol
                int expiryIndex = symbol.IndexOf(expiryStr, StringComparison.OrdinalIgnoreCase);
                if (expiryIndex == -1)
                    return false;

                // Strike is between Expiry and Option Type
                // Start: End of expiry string
                // End: Start of Option Type
                int startIndex = expiryIndex + expiryStr.Length;
                int length = (symbol.Length - 2) - startIndex;

                if (length <= 0)
                    return false;

                string strikeStr = symbol.Substring(startIndex, length);
                
                // Parse strike directly (no normalization needed for symbol string)
                return int.TryParse(strikeStr, out strike);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get the current weekly expiry string for an index (e.g. "12FEB26")
        /// </summary>
        public string GetCurrentWeeklyExpiryStr(string index)
        {
            DayOfWeek expiryDay = DayOfWeek.Thursday;
            if (index.Contains("BANKNIFTY")) expiryDay = DayOfWeek.Wednesday;
            if (index.Contains("FINNIFTY")) expiryDay = DayOfWeek.Tuesday;
            
            DateTime today = DateTime.Today;
            int daysUntil = ((int)expiryDay - (int)today.DayOfWeek + 7) % 7;
            DateTime expiryDate = today.AddDays(daysUntil);
            
            // Format: "13FEB26"
            return expiryDate.ToString("ddMMMyy").ToUpper();
        }

        public async Task<(string token, string symbol)> GetAtmOptionAsync(string index, string optionType, AngelOneDataService dataService)
        {
            try 
            {
                // 1. Get Spot Price
                double spot = await dataService.GetSpotPriceAsync(index);
                if (spot <= 0)
                {
                    Console.WriteLine($"[TokenService] ATM Error: Spot price for {index} is {spot}");
                    return (null, null);
                }

                // 2. Round to ATM Strike
                int strikeStep = index == "NIFTY" ? 50 : 100;
                int atmStrike = (int)(Math.Round(spot / strikeStep) * strikeStep);

                // 3. Try multiple Expiry Formats
                DateTime expiryDate = GetNextExpiry(index);
                var expiryFormats = new List<string> {
                    expiryDate.ToString("ddMMMyy").ToUpper(),   // 26FEB26
                    expiryDate.ToString("ddMMMyyyy").ToUpper(), // 26FEB2026
                    expiryDate.ToString("dMMMyy").ToUpper()     // 6MAR26 (if leading zero is missing)
                };

                Console.WriteLine($"[TokenService] Resolving ATM {index} {optionType} | Spot: {spot} | ATM: {atmStrike} | Expiry: {expiryDate:yyyy-MM-dd}");

                // 4. Find matching instrument
                foreach (var expiryStr in expiryFormats)
                {
                    foreach (var kvp in _symbolToToken)
                    {
                        string symbol = kvp.Key;
                        // Match format: NIFTY + Expiry + Strike + CE/PE
                        if (symbol.StartsWith(index, StringComparison.OrdinalIgnoreCase) && 
                            symbol.Contains(expiryStr) && 
                            symbol.EndsWith(optionType, StringComparison.OrdinalIgnoreCase))
                        {
                            // Precision match for strike: symbol should contain the strike and it should be numeric right before optionType
                            if (symbol.Contains(atmStrike.ToString()))
                            {
                                 Console.WriteLine($"[TokenService] ✓ Success: Found {symbol} for strike {atmStrike}");
                                 return (kvp.Value, symbol);
                            }
                        }
                    }
                }
                
                Console.WriteLine($"[TokenService] ⚠️ Failed to find ATM option for {index} {optionType} {atmStrike} in formats: {string.Join(", ", expiryFormats)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error resolving ATM Option: {ex.Message}");
            }
            return (null, null);
        }


        public class TokenInstrumentInfo
    {
        public string Symbol { get; set; }
        public string Token { get; set; }
        public int Strike { get; set; }
        public string OptionType { get; set; } // "CE" or "PE"
        public int LotSize { get; set; }
    }

    /// <summary>
    /// Get the next authoritative expiry date for an index based on Angel One Scrip Master data.
    /// Eliminates manual day-of-week calculation errors.
    /// </summary>
    public DateTime GetNextExpiry(string index, string type = "Weekly")
    {
        if (_indexExpiries.TryGetValue(index, out var dates))
        {
                // Filter for future dates (Today or later)
                var validDates = dates.Where(d => d.Date >= DateTime.Today.Date).OrderBy(d => d).ToList();
                
                if (!validDates.Any()) 
                {
                    Console.WriteLine($"[TokenService] WARNING: No future expiry dates found for {index} in Scrip Master!");
                    return CalculateFallbackExpiry(index);
                }
                
                if (type.Equals("Weekly", StringComparison.OrdinalIgnoreCase))
                {
                    // Return the nearest expiry
                    return validDates.First();
                }
                else // Monthly
                {
                    // Logic: Group by month/year and pick the last expiry of the nearest month group
                    var nextMonthGroup = validDates.GroupBy(d => new { d.Year, d.Month }).First();
                    return nextMonthGroup.Max();
                }
        }
        
        Console.WriteLine($"[TokenService] WARNING: Index {index} not found in Expiry Cache! Using Fallback.");
        return CalculateFallbackExpiry(index);
    }

    private DateTime CalculateFallbackExpiry(string index)
    {
        DayOfWeek expiryDay = DayOfWeek.Thursday;
        if (index.Contains("BANKNIFTY")) expiryDay = DayOfWeek.Wednesday;
        if (index.Contains("FINNIFTY")) expiryDay = DayOfWeek.Tuesday;
        
        DateTime today = DateTime.Today;
        int daysUntil = ((int)expiryDay - (int)today.DayOfWeek + 7) % 7;
        
        if (daysUntil == 0 && DateTime.Now.Hour >= 15 && DateTime.Now.Minute >= 30)
            daysUntil = 7;
            
        return today.AddDays(daysUntil);
    }
    }


    public class ScripItem
    {
        [JsonProperty("token")]
        public string Token { get; set; }

        [JsonProperty("symbol")]
        public string Symbol { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("expiry")]
        public string Expiry { get; set; }

        [JsonProperty("strike")]
        public string Strike { get; set; }
        
        [JsonProperty("lotsize")]
        public string LotSize { get; set; }
        
        [JsonProperty("instrumenttype")]
        public string InstrumentType { get; set; }
        
        [JsonProperty("exch_seg")]
        public string ExchSeg { get; set; }
        
        [JsonProperty("tick_size")]
        public string TickSize { get; set; }
    }
}
