using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using AlgoTrader.Core.Enums;
using AlgoTrader.Core.Interfaces;
using AlgoTrader.Core.Models;
using Microsoft.Extensions.Logging;

namespace AlgoTrader.OMS;

/// <summary>Professional Execution Engine for handling multi-leg execution, slicing, and retries.</summary>
public class ExecutionEngine
{
    private readonly IOrderManager _orderManager;
    private readonly ILogger<ExecutionEngine> _logger;
    private readonly ConcurrentDictionary<string, OrderBook> _liveOrders = new();
    private readonly Subject<OrderBook> _executionUpdates = new();

    public IObservable<OrderBook> ExecutionUpdates => _executionUpdates.AsObservable();

    // Freeze limits per exchange/symbol (placeholder values, normally fetched from master)
    private readonly Dictionary<string, int> _freezeLimits = new(StringComparer.OrdinalIgnoreCase)
    {
        { "NIFTY", 1800 },
        { "BANKNIFTY", 900 },
        { "FINNIFTY", 1800 },
        { "MIDCPNIFTY", 4200 },
        { "SENSEX", 1000 }
    };

    public ExecutionEngine(IOrderManager orderManager, ILogger<ExecutionEngine> logger)
    {
        _orderManager = orderManager;
        _logger = logger;

        // Listen to broker updates from OrderManager
        _orderManager.OrderUpdates.Subscribe(OnBrokerOrderUpdate);
    }

    private void OnBrokerOrderUpdate(OrderBook update)
    {
        _liveOrders.AddOrUpdate(update.OrderID, update, (key, old) => update);
        _executionUpdates.OnNext(update);
    }

    /// <summary>
    /// Executes a multi-leg strategy entry taking slicing and leg execution type into account.
    /// </summary>
    public async Task<List<string>> ExecuteStrategyAsync(StrategyConfig strategy, AccountCredential account)
    {
        _logger.LogInformation("Executing Strategy {Name} / Legs: {Count}", strategy.Name, strategy.Legs.Count);
        var placedOrderIds = new List<string>();

        // Sort legs: normally for entry, we execute BUY option legs first for margin benefit, then SELL legs
        var orderedLegs = strategy.Legs
            .OrderByDescending(l => l.BuySell == BuySell.BUY)
            .ToList();

        foreach (var leg in orderedLegs)
        {
            try
            {
                var baseQty = leg.Qty > 0 ? leg.Qty : leg.LotMultiplier; // Assuming master handles absolute qty
                
                string baseSymbol = GetUnderlyingFromSymbol(leg.Symbol);
                int limit = _freezeLimits.TryGetValue(baseSymbol, out int val) ? val : 10000;

                var slices = SliceQuantity(baseQty, limit);
                _logger.LogInformation("Leg {Index} ({Symbol} {Action}) sliced into {Slices} orders (Limit: {Limit})", 
                    leg.LegIndex, leg.Symbol, leg.BuySell, slices.Count, limit);

                foreach (var qty in slices)
                {
                    var request = new OrderRequest
                    {
                        Symbol = leg.Symbol,
                        Token = leg.Token,
                        Exchange = leg.Exchange,
                        BuySell = leg.BuySell,
                        Qty = qty,
                        OrderType = leg.OrderType,
                        ProductType = leg.ProductType,
                        LimitPrice = 0, // Market
                        Tag = $"SEQ_{strategy.Id}_{leg.LegIndex}"
                    };

                    var resp = await _orderManager.PlaceAsync(request, account);
                    if (resp.Status == OrderStatus.REJECTED)
                    {
                        _logger.LogError("Order Rejected from Broker: {Message}", resp.StatusMessage);
                    }
                    else
                    {
                        placedOrderIds.Add(resp.OrderID);
                        // Brief pause to prevent rate limiting on multiple slices/legs
                        await Task.Delay(100);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute leg {Index} for Strategy {Name}", leg.LegIndex, strategy.Name);
            }
        }

        return placedOrderIds;
    }

    /// <summary>
    /// Executes an exit for all open legs of a strategy immediately at market price.
    /// </summary>
    public async Task LiquidateStrategyAsync(StrategyConfig strategy, AccountCredential account, List<Position> openPositions)
    {
        _logger.LogInformation("Liquidating Strategy {Name} across {Count} open positions", strategy.Name, openPositions.Count);

        // Group by symbol to offset appropriately
        foreach (var pos in openPositions.Where(p => p.NetQty != 0))
        {
            var exitAction = pos.NetQty > 0 ? BuySell.SELL : BuySell.BUY;
            var absQty = Math.Abs(pos.NetQty);
            
            string baseSymbol = GetUnderlyingFromSymbol(pos.Symbol);
            int limit = _freezeLimits.TryGetValue(baseSymbol, out int val) ? val : 10000;

            var slices = SliceQuantity(absQty, limit);

            foreach (var qty in slices)
            {
                var request = new OrderRequest
                {
                    Symbol = pos.Symbol,
                    Token = pos.Token,
                    Exchange = pos.Exchange,
                    BuySell = exitAction,
                    Qty = qty,
                    OrderType = OrderType.MARKET,
                    ProductType = pos.ProductType,
                    Tag = $"LIQ_{strategy.Id}"
                };

                await _orderManager.PlaceAsync(request, account);
                await Task.Delay(100);
            }
        }
    }

    private List<int> SliceQuantity(int totalQty, int freezeLimit)
    {
        var slices = new List<int>();
        int remaining = totalQty;
        while (remaining > 0)
        {
            int slice = Math.Min(remaining, freezeLimit);
            slices.Add(slice);
            remaining -= slice;
        }
        return slices;
    }

    private string GetUnderlyingFromSymbol(string symbol)
    {
        if (symbol.StartsWith("NIFTY")) return "NIFTY";
        if (symbol.StartsWith("BANKNIFTY")) return "BANKNIFTY";
        if (symbol.StartsWith("FINNIFTY")) return "FINNIFTY";
        if (symbol.StartsWith("MIDCPNIFTY")) return "MIDCPNIFTY";
        if (symbol.StartsWith("SENSEX")) return "SENSEX";
        return symbol; // Default to full symbol if unknown
    }
}
