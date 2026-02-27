using System.Globalization;
using System.IO.Compression;
using AlgoTrader.Core.Enums;
using AlgoTrader.Core.Models;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;

namespace AlgoTrader.Brokers.AngelOne;

/// <summary>Downloads and parses Angel One's instrument master JSON file.</summary>
public class InstrumentMasterService
{
    private static readonly string MASTER_URL = "https://margincalculator.angelbroking.com/OpenAPI_File/files/OpenAPIScripMaster.json";
    private readonly ILogger<InstrumentMasterService> _logger;
    private readonly string _cacheFolder;
    private List<InstrumentMaster> _allInstruments = new();
    private Dictionary<string, InstrumentMaster> _tokenIndex = new();
    private Dictionary<string, List<InstrumentMaster>> _nameIndex = new();
    private bool _isLoaded;

    public bool IsLoaded => _isLoaded;
    public int Count => _allInstruments.Count;

    public InstrumentMasterService(string cacheFolder, ILogger<InstrumentMasterService> logger)
    {
        _cacheFolder = cacheFolder;
        _logger = logger;
        if (!Directory.Exists(_cacheFolder))
            Directory.CreateDirectory(_cacheFolder);
    }

    /// <summary>Downloads instrument master from Angel One or loads from today's cache.</summary>
    public async Task LoadAsync(bool forceRefresh = false)
    {
        var cacheFile = Path.Combine(_cacheFolder, $"instruments_{DateTime.Now:yyyyMMdd}.json");

        string json;

        if (!forceRefresh && File.Exists(cacheFile))
        {
            _logger.LogInformation("Loading instrument master from cache: {File}", cacheFile);
            json = await File.ReadAllTextAsync(cacheFile);
        }
        else
        {
            _logger.LogInformation("Downloading instrument master from Angel One...");
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromMinutes(2);
            json = await http.GetStringAsync(MASTER_URL);

            // Save cache
            await File.WriteAllTextAsync(cacheFile, json);

            // Clean old cache files
            CleanOldCacheFiles(cacheFile);

            _logger.LogInformation("Instrument master downloaded and cached: {Size} bytes", json.Length);
        }

        ParseInstruments(json);
    }

    private void ParseInstruments(string json)
    {
        var rawRecords = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(json);
        if (rawRecords == null)
        {
            _logger.LogError("Failed to parse instrument master JSON");
            return;
        }

        _allInstruments.Clear();
        _tokenIndex.Clear();
        _nameIndex.Clear();

        foreach (var rec in rawRecords)
        {
            try
            {
                var inst = new InstrumentMaster
                {
                    Token = rec.GetValueOrDefault("token", ""),
                    Symbol = rec.GetValueOrDefault("symbol", ""),
                    Name = rec.GetValueOrDefault("name", ""),
                    ExchangeSegment = rec.GetValueOrDefault("exch_seg", ""),
                    InstrumentTypeRaw = rec.GetValueOrDefault("instrumenttype", ""),
                    LotSize = int.TryParse(rec.GetValueOrDefault("lotsize", "1"), out var ls) ? ls : 1,
                    Strike = decimal.TryParse(rec.GetValueOrDefault("strike", "0"), NumberStyles.Any, CultureInfo.InvariantCulture, out var st) ? st / 100m : 0,
                    TickSize = decimal.TryParse(rec.GetValueOrDefault("tick_size", "0.05"), NumberStyles.Any, CultureInfo.InvariantCulture, out var ts) ? ts : 0.05m,
                };

                // Parse expiry
                var expiryStr = rec.GetValueOrDefault("expiry", "");
                if (!string.IsNullOrEmpty(expiryStr))
                {
                    if (DateTime.TryParseExact(expiryStr, new[] { "ddMMMyyyy", "dd-MMM-yyyy", "yyyy-MM-dd" },
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var exp))
                        inst.Expiry = exp;
                }

                // Parse exchange
                inst.Exchange = inst.ExchangeSegment?.ToUpper() switch
                {
                    "NSE" => Exchange.NSE,
                    "NFO" => Exchange.NFO,
                    "BSE" => Exchange.BSE,
                    "MCX" => Exchange.MCX,
                    "CDS" => Exchange.CDS,
                    "BFO" => Exchange.BFO,
                    _ => Exchange.NSE
                };

                // Parse instrument type
                inst.InstrumentType = inst.InstrumentTypeRaw?.ToUpper() switch
                {
                    "FUTSTK" or "FUTIDX" or "FUTCUR" or "FUTCOM" => InstrumentType.FUT,
                    "OPTSTK" or "OPTIDX" or "OPTCUR" or "OPTFUT" => inst.Symbol.EndsWith("CE") ? InstrumentType.CE : InstrumentType.PE,
                    "" => InstrumentType.EQ,
                    _ => InstrumentType.EQ
                };

                // Option type
                if (inst.InstrumentType == InstrumentType.CE)
                    inst.OptionType = "CE";
                else if (inst.InstrumentType == InstrumentType.PE)
                    inst.OptionType = "PE";

                _allInstruments.Add(inst);
                _tokenIndex[inst.Token] = inst;

                var nameKey = inst.Name.ToUpper();
                if (!_nameIndex.ContainsKey(nameKey))
                    _nameIndex[nameKey] = new();
                _nameIndex[nameKey].Add(inst);
            }
            catch (Exception ex)
            {
                // Skip malformed record
            }
        }

        _isLoaded = true;
        _logger.LogInformation("Parsed {Count} instruments from master file", _allInstruments.Count);
    }

    /// <summary>Find instrument by token.</summary>
    public InstrumentMaster? GetByToken(string token)
        => _tokenIndex.GetValueOrDefault(token);

    /// <summary>Find instrument by exact symbol.</summary>
    public InstrumentMaster? GetBySymbol(string symbol)
        => _allInstruments.FirstOrDefault(i => i.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));

    /// <summary>Search instruments by partial name/symbol match.</summary>
    public List<SymbolSearchResult> Search(string query, int maxResults = 20)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2) return new();

        var q = query.ToUpper();
        return _allInstruments
            .Where(i => i.Symbol.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        i.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Take(maxResults)
            .Select(i => new SymbolSearchResult
            {
                Token = i.Token,
                Symbol = i.Symbol,
                Name = i.Name,
                Exchange = i.Exchange.ToString(),
                InstrumentType = i.InstrumentTypeRaw,
                LotSize = i.LotSize
            })
            .ToList();
    }

    /// <summary>Search only equity instruments.</summary>
    public List<SymbolSearchResult> SearchEquity(string query, int maxResults = 20)
    {
        var q = query.ToUpper();
        return _allInstruments
            .Where(i => i.InstrumentType == InstrumentType.EQ &&
                        (i.Exchange == Exchange.NSE || i.Exchange == Exchange.BSE) &&
                        (i.Symbol.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                         i.Name.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .Take(maxResults)
            .Select(i => new SymbolSearchResult
            {
                Token = i.Token, Symbol = i.Symbol, Name = i.Name,
                Exchange = i.Exchange.ToString(), InstrumentType = "EQ", LotSize = 1
            })
            .ToList();
    }

    /// <summary>Get all available expiry dates for an underlying (e.g. "NIFTY").</summary>
    public List<DateTime> GetExpiries(string underlying, Exchange exchange = Exchange.NFO)
    {
        var name = underlying.ToUpper();
        return _allInstruments
            .Where(i => i.Name.ToUpper() == name &&
                        i.Exchange == exchange &&
                        i.Expiry.HasValue &&
                        i.Expiry.Value >= DateTime.Today)
            .Select(i => i.Expiry!.Value)
            .Distinct()
            .OrderBy(d => d)
            .ToList();
    }

    /// <summary>Build an option chain for an underlying at a specific expiry.</summary>
    public OptionChain BuildOptionChain(string underlying, DateTime expiry, Exchange exchange = Exchange.NFO)
    {
        var name = underlying.ToUpper();
        var options = _allInstruments
            .Where(i => i.Name.ToUpper() == name &&
                        i.Exchange == exchange &&
                        i.Expiry.HasValue &&
                        i.Expiry.Value.Date == expiry.Date &&
                        (i.InstrumentType == InstrumentType.CE || i.InstrumentType == InstrumentType.PE))
            .ToList();

        var strikes = options.Select(o => o.Strike).Distinct().OrderBy(s => s).ToList();

        var entries = strikes.Select(strike => new OptionChainEntry
        {
            Strike = strike,
            CE = options.FirstOrDefault(o => o.Strike == strike && o.InstrumentType == InstrumentType.CE),
            PE = options.FirstOrDefault(o => o.Strike == strike && o.InstrumentType == InstrumentType.PE),
        }).ToList();

        return new OptionChain
        {
            Underlying = underlying,
            Expiry = expiry,
            Entries = entries,
            AvailableExpiries = GetExpiries(underlying, exchange)
        };
    }

    /// <summary>Get futures for an underlying.</summary>
    public List<InstrumentMaster> GetFutures(string underlying, Exchange exchange = Exchange.NFO)
    {
        var name = underlying.ToUpper();
        return _allInstruments
            .Where(i => i.Name.ToUpper() == name &&
                        i.Exchange == exchange &&
                        i.InstrumentType == InstrumentType.FUT &&
                        i.Expiry.HasValue &&
                        i.Expiry.Value >= DateTime.Today)
            .OrderBy(i => i.Expiry)
            .ToList();
    }

    /// <summary>Get the nearest futures contract for an underlying.</summary>
    public InstrumentMaster? GetNearMonthFuture(string underlying, Exchange exchange = Exchange.NFO)
        => GetFutures(underlying, exchange).FirstOrDefault();

    /// <summary>Get all instruments for an exchange segment.</summary>
    public List<InstrumentMaster> GetByExchange(Exchange exchange)
        => _allInstruments.Where(i => i.Exchange == exchange).ToList();

    private void CleanOldCacheFiles(string currentFile)
    {
        try
        {
            var files = Directory.GetFiles(_cacheFolder, "instruments_*.json")
                .Where(f => f != currentFile)
                .ToList();
            foreach (var f in files)
                File.Delete(f);
        }
        catch { }
    }
}
