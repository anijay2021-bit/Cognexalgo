using System.Collections.Generic;

namespace AlgoTrader.Core.Models;

// NSE/NFO freeze quantities (single order limit):
public static class FreezeQuantity
{
    // Update these from exchange circulars periodically
    public static readonly Dictionary<string, int> NFO = new()
    {
        ["NIFTY"]    = 1800,  // lots
        ["BANKNIFTY"]= 900,
        ["FINNIFTY"] = 1800,
        ["MIDCPNIFTY"]= 2100,
        ["SENSEX"]   = 1000,
        ["BANKEX"]   = 900,
    };
    
    public static int GetFreezeLots(string symbol)
        => NFO.TryGetValue(symbol.ToUpper(), out var v) ? v : 999;
}
