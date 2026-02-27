using AlgoTrader.Core.Enums;
using Newtonsoft.Json;

namespace AlgoTrader.Core.Models;

/// <summary>Represents an instrument/scrip from the Angel One instrument master.</summary>
public class InstrumentMaster
{
    /// <summary>Angel One symbol token (numeric string).</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Exchange-registered trading symbol, e.g. "NIFTY25FEB24500CE".</summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>Short/display name, e.g. "NIFTY".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Expiry date for derivatives (null for equity).</summary>
    public DateTime? Expiry { get; set; }

    /// <summary>Strike price for options (0 for futures/equity).</summary>
    public decimal Strike { get; set; }

    /// <summary>Lot size (1 for equity).</summary>
    public int LotSize { get; set; } = 1;

    /// <summary>Instrument type: EQ, FUTSTK, FUTIDX, OPTSTK, OPTIDX, etc.</summary>
    public string InstrumentTypeRaw { get; set; } = string.Empty;

    /// <summary>Parsed instrument type enum.</summary>
    public InstrumentType InstrumentType { get; set; }

    /// <summary>Exchange: NSE, NFO, BSE, MCX, CDS.</summary>
    public Exchange Exchange { get; set; }

    /// <summary>Exchange segment raw string from master file.</summary>
    public string ExchangeSegment { get; set; } = string.Empty;

    /// <summary>Tick size (minimum price movement).</summary>
    public decimal TickSize { get; set; }

    /// <summary>Option type: CE, PE, or empty for futures/equity.</summary>
    public string OptionType { get; set; } = string.Empty;

    /// <summary>Formatted display string.</summary>
    public override string ToString() => $"{Symbol} ({Exchange}) [{InstrumentTypeRaw}]";
}

/// <summary>Option chain entry — groups CE and PE at same strike.</summary>
public class OptionChainEntry
{
    public decimal Strike { get; set; }
    public InstrumentMaster? CE { get; set; }
    public InstrumentMaster? PE { get; set; }
    public decimal? CELtp { get; set; }
    public decimal? PELtp { get; set; }
    public long? CEOI { get; set; }
    public long? PEOI { get; set; }
}

/// <summary>Complete option chain for an underlying at a specific expiry.</summary>
public class OptionChain
{
    public string Underlying { get; set; } = string.Empty;
    public DateTime Expiry { get; set; }
    public decimal SpotPrice { get; set; }
    public decimal AtmStrike { get; set; }
    public List<OptionChainEntry> Entries { get; set; } = new();
    public List<DateTime> AvailableExpiries { get; set; } = new();
}

/// <summary>Symbol search result for UI autocomplete.</summary>
public class SymbolSearchResult
{
    public string Token { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public string InstrumentType { get; set; } = string.Empty;
    public int LotSize { get; set; }
    public string DisplayText => $"{Symbol} | {Exchange} | {InstrumentType} | Lot: {LotSize}";
}
