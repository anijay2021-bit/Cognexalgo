using AlgoTrader.Core.Enums;
using Newtonsoft.Json;
using LiteDB;

namespace AlgoTrader.Core.Models;

/// <summary>Broker account credentials and authentication state.</summary>
public class AccountCredential
{
    /// <summary>Broker-assigned client ID.</summary>
    [BsonId]
    public string ClientID { get; set; } = string.Empty;

    /// <summary>Login password (encrypted at rest).</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>4-digit MPIN / PIN.</summary>
    public string PIN { get; set; } = string.Empty;

    /// <summary>API key from broker developer portal.</summary>
    public string APIKey { get; set; } = string.Empty;

    /// <summary>API secret from broker developer portal.</summary>
    public string APISecret { get; set; } = string.Empty;

    /// <summary>Base32 TOTP secret for auto-login.</summary>
    public string TOTPSecret { get; set; } = string.Empty;

    /// <summary>Broker type for this account.</summary>
    public BrokerType BrokerType { get; set; }

    /// <summary>JWT auth token after login.</summary>
    [JsonIgnore]
    public string JWTToken { get; set; } = string.Empty;

    /// <summary>Refresh token for re-auth.</summary>
    [JsonIgnore]
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>Feed token for WebSocket auth (Angel One).</summary>
    [JsonIgnore]
    public string FeedToken { get; set; } = string.Empty;

    /// <summary>When the JWT expires.</summary>
    public DateTime TokenExpiry { get; set; }

    /// <summary>Whether currently authenticated.</summary>
    [JsonIgnore]
    public bool IsLoggedIn { get; set; }

    /// <summary>Human-readable account name.</summary>
    public string AccountName { get; set; } = string.Empty;

    /// <summary>Group for multi-account strategies.</summary>
    public string GroupName { get; set; } = string.Empty;
}

/// <summary>Real-time market tick from WebSocket feed.</summary>
public class Tick
{
    /// <summary>Instrument token (exchange-specific).</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Trading symbol.</summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>Last traded price.</summary>
    public decimal LTP { get; set; }

    /// <summary>Best bid price.</summary>
    public decimal BidPrice { get; set; }

    /// <summary>Best ask price.</summary>
    public decimal AskPrice { get; set; }

    /// <summary>Bid quantity.</summary>
    public long BidQty { get; set; }

    /// <summary>Ask quantity.</summary>
    public long AskQty { get; set; }

    /// <summary>Traded volume for the day.</summary>
    public long Volume { get; set; }

    /// <summary>Open interest (for derivatives).</summary>
    public long OI { get; set; }

    /// <summary>Exchange timestamp.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Exchange of this tick.</summary>
    public Exchange Exchange { get; set; }
}

/// <summary>OHLCV candle for historical/live data.</summary>
public class Candle
{
    /// <summary>Trading symbol.</summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>Instrument token.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Candle interval.</summary>
    public TimeFrame Interval { get; set; }

    /// <summary>Open price.</summary>
    public decimal Open { get; set; }

    /// <summary>High price.</summary>
    public decimal High { get; set; }

    /// <summary>Low price.</summary>
    public decimal Low { get; set; }

    /// <summary>Close price.</summary>
    public decimal Close { get; set; }

    /// <summary>Traded volume.</summary>
    public long Volume { get; set; }

    /// <summary>Open interest.</summary>
    public long OI { get; set; }

    /// <summary>Candle open timestamp.</summary>
    public DateTime Timestamp { get; set; }
}

/// <summary>Order placement request to broker.</summary>
public record OrderRequest
{
    /// <summary>Trading symbol name.</summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>Instrument token.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Exchange.</summary>
    public Exchange Exchange { get; set; }

    /// <summary>Buy or Sell.</summary>
    public BuySell BuySell { get; set; }

    /// <summary>Order quantity.</summary>
    public int Qty { get; set; }

    /// <summary>Order type (Market/Limit/SL/SL-M).</summary>
    public OrderType OrderType { get; set; }

    /// <summary>Product type (CNC/NRML/MIS).</summary>
    public ProductType ProductType { get; set; }

    /// <summary>Limit price (for LIMIT/SL orders).</summary>
    public decimal LimitPrice { get; set; }

    /// <summary>Trigger price (for SL/SL-M orders).</summary>
    public decimal TriggerPrice { get; set; }

    /// <summary>Order validity (DAY/IOC).</summary>
    public string Validity { get; set; } = "DAY";

    /// <summary>Custom tag for order tracking.</summary>
    public string Tag { get; set; } = string.Empty;
}

/// <summary>Broker response after order placement.</summary>
public class OrderResponse
{
    /// <summary>Broker-assigned order ID.</summary>
    public string OrderID { get; set; } = string.Empty;

    /// <summary>Order status.</summary>
    public OrderStatus Status { get; set; }

    /// <summary>Status message from broker.</summary>
    public string StatusMessage { get; set; } = string.Empty;

    /// <summary>Response timestamp.</summary>
    public DateTime Timestamp { get; set; }
}

/// <summary>Complete order book entry with all fields.</summary>
public class OrderBook
{
    public string OrderID { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public Exchange Exchange { get; set; }
    public BuySell BuySell { get; set; }
    public int OrderQty { get; set; }
    public int FilledQty { get; set; }
    public decimal AvgPrice { get; set; }
    public decimal LimitPrice { get; set; }
    public decimal TriggerPrice { get; set; }
    public OrderType OrderType { get; set; }
    public ProductType ProductType { get; set; }
    public OrderStatus Status { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
    public DateTime? ExchangeTime { get; set; }
    public DateTime PlaceTime { get; set; }
    public DateTime OrderTime { get; set; }
    public BrokerType Broker { get; set; }
    public decimal Strike { get; set; }
    public InstrumentType InstrumentType { get; set; }
    public int DTE { get; set; }
    public string Token { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
}

/// <summary>Current position held on broker.</summary>
public class Position
{
    public string Symbol { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public Exchange Exchange { get; set; }
    public ProductType ProductType { get; set; }
    public int NetQty { get; set; }
    public int BuyQty { get; set; }
    public int SellQty { get; set; }
    public decimal BuyAvg { get; set; }
    public decimal SellAvg { get; set; }
    public decimal LTP { get; set; }
    public decimal MTM { get; set; }
    public decimal RealizedPnL { get; set; }
    public BrokerType BrokerType { get; set; }
}

/// <summary>Index instrument info with active expiry dates.</summary>
public class IndexInfo
{
    public string Name { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public Exchange Exchange { get; set; }
    public decimal LTP { get; set; }
    public List<DateTime> ExpiryDates { get; set; } = new();
}

/// <summary>Account funds/margin data from broker.</summary>
public class FundsData
{
    public decimal AvailableMargin { get; set; }
    public decimal UsedMargin { get; set; }
    public decimal TotalMargin { get; set; }
    public BrokerType BrokerType { get; set; }
}

/// <summary>User profile returned by broker.</summary>
public class UserProfile
{
    public string ClientId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Broker { get; set; } = string.Empty;
    public List<string> Exchanges { get; set; } = new();
}

/// <summary>
/// Equity / ETF holding in the demat account.
/// Returned by IBroker.GetHoldingsAsync() for portfolio P&amp;L calculations.
/// </summary>
public class HoldingRecord
{
    public string TradingSymbol { get; set; } = string.Empty;
    public string ISIN          { get; set; } = string.Empty;
    public string Exchange      { get; set; } = string.Empty;
    public int    Qty           { get; set; }
    public int    AuthorisedQty { get; set; }
    public decimal AvgPrice     { get; set; }
    public decimal LTP          { get; set; }
    public decimal PnL          { get; set; }
    public decimal PnLPercent   { get; set; }
}

/// <summary>
/// A completed/filled trade entry from the broker's trade book.
/// Returned by IBroker.GetTradeBookAsync() — distinct from OrderBook which
/// includes pending orders.
/// </summary>
public class TradeRecord
{
    public string OrderId          { get; set; } = string.Empty;
    public string TradeId          { get; set; } = string.Empty;
    public string TradingSymbol    { get; set; } = string.Empty;
    public string Exchange         { get; set; } = string.Empty;
    public string TransactionType  { get; set; } = string.Empty; // BUY / SELL
    public int    Qty              { get; set; }
    public decimal FillPrice       { get; set; }
    public DateTime FillTime       { get; set; }
    public string ProductType      { get; set; } = string.Empty;
}
