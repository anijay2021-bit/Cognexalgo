using AlgoTrader.Core.Models;
using AlgoTrader.Core.Enums;
using AlgoTrader.Core.Interfaces;

namespace AlgoTrader.OMS;

public class SmartOrderExecutor
{
    private readonly IOrderManager _orderManager;
    
    public SmartOrderExecutor(IOrderManager orderManager)
    {
        _orderManager = orderManager;
    }
    
    public async Task<OrderResponse> ExecuteWithAdvancedSettingsAsync(
        OrderRequest baseRequest, AdvancedOrderConfig config,
        decimal currentLTP, AccountCredential account, bool isEntry)
    {
        var execType = isEntry ? config.EntryOrderType : config.ExitOrderType;
        var bufferType = isEntry ? config.EntryBufferType : config.ExitBufferType;
        var bufferVal = isEntry ? config.EntryBufferValue : config.ExitBufferValue;
        var timeoutSecs = isEntry ? config.EntryConvertToMarketAfterSecs : config.ExitConvertToMarketAfterSecs;
        var delayType = isEntry ? config.EntryDelayType : config.ExitDelayType;
        var delaySecs = isEntry ? config.EntryDelaySeconds : config.ExitDelaySeconds;
        
        // Apply entry/exit delay
        if (delaySecs > 0)
        {
            bool isBuy = baseRequest.BuySell == BuySell.BUY;
            bool shouldDelay = delayType switch
            {
                OrderDelayType.DelayBuyPositions  => isBuy,
                OrderDelayType.DelaySellPositions => !isBuy,
                OrderDelayType.DelayAll           => true,
                _ => false
            };
            if (shouldDelay) await Task.Delay(TimeSpan.FromSeconds(delaySecs));
        }
        
        // Calculate limit price with buffer
        decimal limitPrice = currentLTP;
        if (execType != OrderExecType.Market)
        {
            decimal buffer = bufferType == BufferType.Percentage
                ? currentLTP * bufferVal / 100
                : bufferVal;
            limitPrice = baseRequest.BuySell == BuySell.BUY
                ? currentLTP + buffer   // buy: pay slightly more (better fill chance)
                : currentLTP - buffer;  // sell: accept slightly less
        }
        
        var request = baseRequest with {
            OrderType = execType == OrderExecType.Market ? OrderType.MARKET : OrderType.LIMIT,
            LimitPrice = limitPrice
        };
        
        var response = await _orderManager.PlaceAsync(request, account);
        
        // Convert to market if not filled after N seconds
        if (timeoutSecs > 0 && response.Status == OrderStatus.OPEN)
        {
            _ = Task.Run(async () => {
                await Task.Delay(TimeSpan.FromSeconds(timeoutSecs));
                var status = await _orderManager.GetOrderStatusAsync(response.OrderID, account);
                if (status == OrderStatus.OPEN)
                {
                    await _orderManager.ModifyToMarketAsync(response.OrderID, account);
                }
            });
        }
        return response;
    }
    
    // PriceCalcFrom: determine SL/Target reference price
    public decimal GetReferencePrice(
        PriceCalcFrom calcFrom, LegPosition position, decimal currentLTP)
        => calcFrom == PriceCalcFrom.AveragePrice ? position.AvgEntryPrice : currentLTP;
}
