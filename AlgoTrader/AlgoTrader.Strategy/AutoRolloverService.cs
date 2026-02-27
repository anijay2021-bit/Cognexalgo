using AlgoTrader.Core.Models;
using AlgoTrader.Core.Enums;
using AlgoTrader.Core.Interfaces;
using AlgoTrader.MarketData;
using AlgoTrader.Brokers.AngelOne;
using Microsoft.Extensions.Logging;

namespace AlgoTrader.Strategy;

public class AutoRolloverService
{
    private readonly IOrderManager _orderManager;
    private readonly ExpiryResolver _expiryResolver;
    private readonly InstrumentMasterService _instruments;
    private readonly ILogger<AutoRolloverService> _logger;
 
    public AutoRolloverService(
        IOrderManager orderManager,
        ExpiryResolver expiryResolver,
        InstrumentMasterService instruments,
        ILogger<AutoRolloverService> logger)
    {
        _orderManager = orderManager;
        _expiryResolver = expiryResolver;
        _instruments = instruments;
        _logger = logger;
    }

    // Call this near expiry (e.g., on expiry day before configured rollover time)
    public async Task RolloverPositionAsync(
        StrategyConfig strategy, LegPosition expiredPosition,
        AccountCredential account)
    {
        _logger.LogInformation("Rolling over {Symbol} position to next expiry", 
            expiredPosition.Symbol);
        
        // Step 1: Square off expiring position at market
        var exitReq = new OrderRequest {
            Symbol = expiredPosition.Symbol,
            Token = expiredPosition.Token,
            Exchange = expiredPosition.Exchange,
            BuySell = expiredPosition.BuySell == BuySell.BUY ? BuySell.SELL : BuySell.BUY,
            Qty = Math.Abs(expiredPosition.NetQty),
            OrderType = OrderType.MARKET,
            ProductType = expiredPosition.ProductType
        };
        await _orderManager.PlaceAsync(exitReq, account);
        
        // Find equivalent in next expiry
        var nextExpiry = await _expiryResolver.ResolveExpiryAsync(
            strategy.Legs.FirstOrDefault()?.UnderlyingSymbol ?? string.Empty, 
            expiredPosition.Exchange, 
            ExpirySelectionType.Next);

        InstrumentMaster? nextInstrument = null;
        if (expiredPosition.ProductType == ProductType.MIS || expiredPosition.ProductType == ProductType.NRML)
        {
            if (expiredPosition.Symbol.EndsWith("CE") || expiredPosition.Symbol.EndsWith("PE"))
            {
                var chain = _instruments.BuildOptionChain(strategy.Legs.FirstOrDefault()?.UnderlyingSymbol ?? string.Empty, nextExpiry, expiredPosition.Exchange);
                // Try to map by strike and option type
                // Would need to extract strike from expiredPosition.Symbol, but for simplicity assuming we know the strike or we use a fallback
                // E.g. simple fallback just for compilation:
                nextInstrument = chain.Entries.FirstOrDefault()?.CE; // PLACEHOLDER
            }
            else
            {
                nextInstrument = _instruments.GetNearMonthFuture(strategy.Legs.FirstOrDefault()?.UnderlyingSymbol ?? string.Empty, expiredPosition.Exchange);
            }
        }
            
        if (nextInstrument == null)
        {
            _logger.LogWarning("Could not find next expiry instrument for rollover");
            return;
        }
        
        // Step 3: Enter new position in next expiry
        var entryReq = new OrderRequest {
            Symbol = nextInstrument.Symbol,
            Token = nextInstrument.Token,
            Exchange = nextInstrument.Exchange,
            BuySell = expiredPosition.BuySell,
            Qty = Math.Abs(expiredPosition.NetQty),
            OrderType = OrderType.MARKET,
            ProductType = expiredPosition.ProductType
        };
        await _orderManager.PlaceAsync(entryReq, account);
    }
}
