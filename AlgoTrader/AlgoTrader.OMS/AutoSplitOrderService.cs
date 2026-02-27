using AlgoTrader.Core.Models;
using AlgoTrader.Core.Enums;

namespace AlgoTrader.OMS;

public class AutoSplitOrderService
{
    private readonly SmartOrderExecutor _executor;
    
    public AutoSplitOrderService(SmartOrderExecutor executor)
    {
        _executor = executor;
    }

    // Splits large order into clips at or below freeze quantity
    public async Task<List<OrderResponse>> PlaceSplitOrderAsync(
        OrderRequest request, AdvancedOrderConfig config,
        decimal currentLTP, AccountCredential account, bool isEntry,
        string underlyingSymbol)
    {
        int freezeLots = FreezeQuantity.GetFreezeLots(underlyingSymbol);
        int totalLots = request.Qty;
        var responses = new List<OrderResponse>();
        
        if (totalLots <= freezeLots)
        {
            // No split needed
            responses.Add(await _executor.ExecuteWithAdvancedSettingsAsync(
                request, config, currentLTP, account, isEntry));
            return responses;
        }
        
        // Split into clips
        int remaining = totalLots;
        while (remaining > 0)
        {
            int clipQty = Math.Min(remaining, freezeLots);
            var clipRequest = request with { Qty = clipQty };
            var resp = await _executor.ExecuteWithAdvancedSettingsAsync(
                clipRequest, config, currentLTP, account, isEntry);
            responses.Add(resp);
            remaining -= clipQty;
            
            if (remaining > 0)
                await Task.Delay(200); // small delay between clips
        }
        return responses;
    }
}
