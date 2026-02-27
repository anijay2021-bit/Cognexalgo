using AlgoTrader.Core.Models;
using AlgoTrader.Core.Enums;
using AlgoTrader.Core.Interfaces;
using AlgoTrader.Brokers.AngelOne;
using AlgoTrader.Core.Interfaces;

namespace AlgoTrader.Strategy;

public class PremiumStrikeSelector
{
    private readonly IMarketDataService _marketData;
    private readonly InstrumentMasterService _instruments;
    
    public PremiumStrikeSelector(IMarketDataService marketData, InstrumentMasterService instruments)
    {
        _marketData = marketData;
        _instruments = instruments;
    }
 
    // Finds the strike whose current LTP (premium) best matches the criteria
    public async Task<InstrumentMaster?> SelectStrikeByPremiumAsync(
        LegConfig leg, DateTime expiry, Exchange exchange, string authToken)
    {
        // Get all strikes for this symbol/expiry/optionType
        var chain = _instruments.BuildOptionChain(leg.UnderlyingSymbol, expiry, exchange);
        
        var filtered = leg.OptionType == "CE"
            ? chain.Entries.Select(e => e.CE).Where(e => e != null).ToList()
            : chain.Entries.Select(e => e.PE).Where(e => e != null).ToList();
        
        // Fetch LTPs for all strikes in the chain (batch LTP call)
        var tokens = filtered.Select(i => (exchange, i.Token)).ToList();
        var ltps = await _marketData.GetBatchLTPAsync(tokens, authToken);
        
        return leg.PremiumSelectionType switch
        {
            PremiumSelectionType.CloseTo =>
                filtered.MinBy(i => Math.Abs((ltps.GetValueOrDefault(i.Token)) - leg.PremiumTargetValue)),
            PremiumSelectionType.GreaterThan =>
                filtered.Where(i => ltps.GetValueOrDefault(i.Token) > leg.PremiumTargetValue)
                        .MinBy(i => ltps.GetValueOrDefault(i.Token)),
            PremiumSelectionType.LessThan =>
                filtered.Where(i => ltps.GetValueOrDefault(i.Token) < leg.PremiumTargetValue)
                        .MaxBy(i => ltps.GetValueOrDefault(i.Token)),
            _ => null
        };
    }
}
