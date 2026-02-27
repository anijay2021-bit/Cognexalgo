using System.Reactive.Linq;
using System.Reactive.Subjects;
using AlgoTrader.Core.Enums;
using AlgoTrader.Core.Interfaces;
using AlgoTrader.Core.Models;
using Microsoft.Extensions.Logging;

namespace AlgoTrader.OMS;

/// <summary>Order Manager — places orders via broker and tracks updates.</summary>
public class OrderManager : IOrderManager
{
    private readonly IBrokerFactory _brokerFactory;
    private readonly ILogger<OrderManager> _logger;
    private readonly Subject<OrderBook> _orderUpdates = new();

    public IObservable<OrderBook> OrderUpdates => _orderUpdates.AsObservable();

    public OrderManager(IBrokerFactory brokerFactory, ILogger<OrderManager> logger)
    {
        _brokerFactory = brokerFactory;
        _logger = logger;
    }

    public async Task<OrderResponse> PlaceAsync(OrderRequest request, AccountCredential account)
    {
        var broker = _brokerFactory.Create(account.BrokerType);
        _logger.LogInformation("Placing {BuySell} order: {Symbol} x{Qty} [{Type}]",
            request.BuySell, request.Symbol, request.Qty, request.OrderType);

        var response = await broker.PlaceOrderAsync(request, account.JWTToken);
        _logger.LogInformation("Order placed: {OrderID} status={Status}", response.OrderID, response.Status);
        return response;
    }

    public async Task<OrderResponse> ModifyAsync(string orderId, OrderRequest request, AccountCredential account)
    {
        var broker = _brokerFactory.Create(account.BrokerType);
        var response = await broker.ModifyOrderAsync(orderId, request, account.JWTToken);
        _logger.LogInformation("Order modified: {OrderID} status={Status}", orderId, response.Status);
        return response;
    }

    public async Task<OrderResponse> CancelAsync(string orderId, AccountCredential account)
    {
        var broker = _brokerFactory.Create(account.BrokerType);
        var response = await broker.CancelOrderAsync(orderId, account.JWTToken);
        _logger.LogInformation("Order cancelled: {OrderID}", orderId);
        return response;
    }

    public async Task SyncOrderBookAsync(AccountCredential account)
    {
        var broker = _brokerFactory.Create(account.BrokerType);
        var orders = await broker.GetOrderBookAsync(account.JWTToken);
        foreach (var order in orders)
            _orderUpdates.OnNext(order);
        _logger.LogDebug("Synced {Count} orders from broker", orders.Count);
    }

    public async Task<OrderStatus> GetOrderStatusAsync(string orderId, AccountCredential account)
    {
        var broker = _brokerFactory.Create(account.BrokerType);
        var orders = await broker.GetOrderBookAsync(account.JWTToken);
        var order = orders.FirstOrDefault(o => o.OrderID == orderId);
        return order?.Status ?? OrderStatus.OPEN;
    }

    public async Task<OrderResponse> ModifyToMarketAsync(string orderId, AccountCredential account)
    {
        var broker = _brokerFactory.Create(account.BrokerType);
        var orders = await broker.GetOrderBookAsync(account.JWTToken);
        var order = orders.FirstOrDefault(o => o.OrderID == orderId);
        if (order == null) return new OrderResponse { Status = OrderStatus.REJECTED, StatusMessage = "Order not found" };

        var request = new OrderRequest
        {
            Symbol = order.Symbol,
            Token = order.Token,
            Exchange = order.Exchange,
            BuySell = order.BuySell,
            Qty = order.OrderQty,
            OrderType = OrderType.MARKET,
            ProductType = order.ProductType
        };

        return await broker.ModifyOrderAsync(orderId, request, account.JWTToken);
    }
}

/// <summary>Background sync service — polls broker for order/position updates.</summary>
public class SyncService : IDisposable
{
    private readonly IOrderManager _orderManager;
    private readonly ILogger<SyncService> _logger;
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;
    private readonly List<AccountCredential> _accounts = new();

    public SyncService(IOrderManager orderManager, ILogger<SyncService> logger)
    {
        _orderManager = orderManager;
        _logger = logger;
    }

    public void AddAccount(AccountCredential account) => _accounts.Add(account);

    public void Start(int intervalMs = 5000)
    {
        _cts = new CancellationTokenSource();
        _timer = new PeriodicTimer(TimeSpan.FromMilliseconds(intervalMs));

        _ = Task.Run(async () =>
        {
            while (await _timer.WaitForNextTickAsync(_cts.Token))
            {
                foreach (var account in _accounts.Where(a => a.IsLoggedIn))
                {
                    try
                    {
                        await _orderManager.SyncOrderBookAsync(account);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Sync failed for {ClientID}", account.ClientID);
                    }
                }
            }
        });

        _logger.LogInformation("SyncService started — polling every {Interval}ms", intervalMs);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _timer?.Dispose();
        _logger.LogInformation("SyncService stopped");
    }

    public void Dispose() => Stop();
}
