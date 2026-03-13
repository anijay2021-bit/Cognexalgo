using System.Reactive.Linq;
using System.Reactive.Subjects;
using AlgoTrader.Core.Enums;
using AlgoTrader.Core.Interfaces;
using AlgoTrader.Core.Models;
using Microsoft.Extensions.Logging;

namespace AlgoTrader.Brokers;

/// <summary>Paper/simulated trading broker — logs orders locally without hitting any real API.</summary>
public class PaperTradingBroker : IBroker
{
    private readonly ILogger<PaperTradingBroker> _logger;
    private readonly Dictionary<string, OrderBook> _orders = new();
    private readonly List<Position> _positions = new();
    private int _nextOrderId = 10000;
    private decimal _balance = 1_000_000m; // ₹10L paper balance

    public BrokerType BrokerType => BrokerType.AngelOne; // Pretends to be AngelOne

    public PaperTradingBroker(ILogger<PaperTradingBroker> logger)
    {
        _logger = logger;
    }

    public Task<bool> LoginAsync(AccountCredential credential)
    {
        credential.JWTToken = "PAPER_TOKEN";
        credential.RefreshToken = "PAPER_REFRESH";
        credential.FeedToken = "PAPER_FEED";
        credential.IsLoggedIn = true;
        credential.TokenExpiry = DateTime.UtcNow.AddDays(365);
        _logger.LogInformation("📄 Paper Trading login successful for {ClientID}", credential.ClientID);
        return Task.FromResult(true);
    }

    public Task<bool> RefreshTokenAsync(AccountCredential credential)
    {
        credential.TokenExpiry = DateTime.UtcNow.AddDays(365);
        return Task.FromResult(true);
    }

    public Task<UserProfile> GetProfileAsync(string token) => Task.FromResult(new UserProfile
    {
        ClientId = "PAPER001",
        Name = "Paper Trading Account",
        Email = "paper@algotrader.local",
        Broker = "PaperTrading",
        Exchanges = new List<string> { "NSE", "NFO", "BSE", "MCX" }
    });

    public Task<FundsData> GetFundsAsync(string token) => Task.FromResult(new FundsData
    {
        AvailableMargin = _balance,
        UsedMargin = 1_000_000m - _balance,
        TotalMargin = 1_000_000m,
        BrokerType = BrokerType.AngelOne
    });

    public Task<OrderResponse> PlaceOrderAsync(OrderRequest request, string token)
    {
        var orderId = $"PAPER_{_nextOrderId++}";
        var order = new OrderBook
        {
            OrderID = orderId,
            Symbol = request.Symbol,
            Token = request.Token,
            Exchange = request.Exchange,
            BuySell = request.BuySell,
            OrderQty = request.Qty,
            FilledQty = request.Qty, // Instant fill in paper mode
            AvgPrice = request.LimitPrice > 0 ? request.LimitPrice : 0,
            LimitPrice = request.LimitPrice,
            TriggerPrice = request.TriggerPrice,
            OrderType = request.OrderType,
            ProductType = request.ProductType,
            Status = OrderStatus.COMPLETE,
            StatusMessage = "Paper order filled",
            PlaceTime = DateTime.UtcNow,
            OrderTime = DateTime.UtcNow,
            Broker = BrokerType.AngelOne,
            Tag = request.Tag
        };
        _orders[orderId] = order;

        // Update position
        UpdatePosition(request);

        _logger.LogInformation("📄 Paper order: {BuySell} {Qty} {Symbol} @ {Price} → {OrderID}",
            request.BuySell, request.Qty, request.Symbol, request.LimitPrice, orderId);

        return Task.FromResult(new OrderResponse
        {
            OrderID = orderId,
            Status = OrderStatus.COMPLETE,
            StatusMessage = "Paper order filled instantly",
            Timestamp = DateTime.UtcNow
        });
    }

    public Task<OrderResponse> ModifyOrderAsync(string orderId, OrderRequest request, string token)
    {
        if (_orders.TryGetValue(orderId, out var order))
        {
            order.LimitPrice = request.LimitPrice;
            order.TriggerPrice = request.TriggerPrice;
            order.OrderQty = request.Qty;
            order.Status = OrderStatus.MODIFIED;
            _logger.LogInformation("📄 Paper order modified: {OrderID}", orderId);
        }

        return Task.FromResult(new OrderResponse
        {
            OrderID = orderId,
            Status = OrderStatus.MODIFIED,
            StatusMessage = "Paper order modified",
            Timestamp = DateTime.UtcNow
        });
    }

    public Task<OrderResponse> CancelOrderAsync(string orderId, string token)
    {
        if (_orders.TryGetValue(orderId, out var order))
        {
            order.Status = OrderStatus.CANCELLED;
            _logger.LogInformation("📄 Paper order cancelled: {OrderID}", orderId);
        }

        return Task.FromResult(new OrderResponse
        {
            OrderID = orderId,
            Status = OrderStatus.CANCELLED,
            StatusMessage = "Paper order cancelled",
            Timestamp = DateTime.UtcNow
        });
    }

    public Task<List<OrderBook>> GetOrderBookAsync(string token)
        => Task.FromResult(_orders.Values.ToList());

    public Task<List<Position>> GetPositionBookAsync(string token)
        => Task.FromResult(_positions.ToList());

    public Task<List<Candle>> GetHistoricalDataAsync(string token, string symbolToken, Exchange exchange, TimeFrame interval, DateTime from, DateTime to)
        => Task.FromResult(new List<Candle>()); // Paper mode uses real historical data from DB

    public Task<decimal> GetLTPAsync(string symbolToken, Exchange exchange, string authToken)
        => Task.FromResult(0m); // LTP comes from live WebSocket feed

    private void UpdatePosition(OrderRequest request)
    {
        var existing = _positions.FirstOrDefault(p => p.Token == request.Token);
        if (existing == null)
        {
            existing = new Position
            {
                Symbol      = request.Symbol,
                Token       = request.Token,
                Exchange    = request.Exchange,
                ProductType = request.ProductType,
                BrokerType  = BrokerType.AngelOne
            };
            _positions.Add(existing);
        }

        decimal fillPrice = request.LimitPrice > 0 ? request.LimitPrice : 0;

        if (request.BuySell == BuySell.BUY)
        {
            // Weighted average buy price
            decimal totalCost = existing.BuyAvg * existing.BuyQty + fillPrice * request.Qty;
            existing.BuyQty  += request.Qty;
            existing.BuyAvg   = existing.BuyQty > 0 ? totalCost / existing.BuyQty : fillPrice;
            existing.NetQty  += request.Qty;
            _balance         -= fillPrice * request.Qty;
        }
        else
        {
            decimal totalCost = existing.SellAvg * existing.SellQty + fillPrice * request.Qty;
            existing.SellQty += request.Qty;
            existing.SellAvg  = existing.SellQty > 0 ? totalCost / existing.SellQty : fillPrice;
            existing.NetQty  -= request.Qty;
            _balance         += fillPrice * request.Qty;
        }
    }

    // ── Portfolio stubs (paper broker has no real demat / trade book) ────────

    public Task<List<HoldingRecord>> GetHoldingsAsync(string token)
        => Task.FromResult(new List<HoldingRecord>());

    public Task<List<TradeRecord>> GetTradeBookAsync(string token)
    {
        // Map paper-trade filled orders to TradeRecord so callers get consistent data
        var fills = _orders.Values
            .Where(o => o.Status == OrderStatus.COMPLETE)
            .Select(o => new TradeRecord
            {
                OrderId         = o.OrderID,
                TradeId         = o.OrderID,
                TradingSymbol   = o.Symbol,
                Exchange        = o.Exchange.ToString(),
                TransactionType = o.BuySell.ToString(),
                Qty             = o.FilledQty,
                FillPrice       = o.AvgPrice,
                FillTime        = o.PlaceTime,
                ProductType     = o.ProductType.ToString()
            })
            .ToList();
        return Task.FromResult(fills);
    }
}
