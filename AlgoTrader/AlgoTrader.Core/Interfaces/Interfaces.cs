using AlgoTrader.Core.Enums;
using AlgoTrader.Core.Models;

namespace AlgoTrader.Core.Interfaces;

/// <summary>Abstraction for broker REST API operations.</summary>
public interface IBroker
{
    BrokerType BrokerType { get; }
    Task<bool> LoginAsync(AccountCredential credential);
    Task<bool> RefreshTokenAsync(AccountCredential credential);
    Task<UserProfile> GetProfileAsync(string token);
    Task<FundsData> GetFundsAsync(string token);
    Task<OrderResponse> PlaceOrderAsync(OrderRequest request, string token);
    Task<OrderResponse> ModifyOrderAsync(string orderId, OrderRequest request, string token);
    Task<OrderResponse> CancelOrderAsync(string orderId, string token);
    Task<List<OrderBook>> GetOrderBookAsync(string token);
    Task<List<Position>> GetPositionBookAsync(string token);
    Task<List<Candle>> GetHistoricalDataAsync(string token, string symbolToken, Exchange exchange, TimeFrame interval, DateTime from, DateTime to);
    Task<decimal> GetLTPAsync(string symbolToken, Exchange exchange, string authToken);
}

/// <summary>Factory to create broker instances by type.</summary>
public interface IBrokerFactory
{
    IBroker Create(BrokerType brokerType);
    IBroker CreateFromCredential(AccountCredential credential);
}

/// <summary>Live market data WebSocket feed.</summary>
public interface IMarketDataService
{
    Task ConnectAsync(AccountCredential credential);
    Task DisconnectAsync();
    Task SubscribeAsync(List<(Exchange exchange, string token)> symbols, SubscriptionMode mode);
    Task UnsubscribeAsync(List<(Exchange exchange, string token)> symbols);
    Task<Dictionary<string, decimal>> GetBatchLTPAsync(List<(Exchange exchange, string token)> tokens, string authToken);
    IObservable<Tick> TickStream { get; }
    bool IsConnected { get; }
}

/// <summary>Strategy lifecycle management.</summary>
public interface IStrategyEngine
{
    Task StartAsync();
    Task StopAsync();
    Task RegisterStrategyAsync(StrategyConfig config);
    Task RemoveStrategyAsync(string strategyId);
    Task StartStrategyAsync(Guid strategyId);
    Task StopStrategyAsync(Guid strategyId);
    Task ExitStrategyAsync(Guid strategyId, ExitReason reason);
    Task SquareOffAllAsync();
}

/// <summary>Order placement and tracking.</summary>
public interface IOrderManager
{
    Task<OrderResponse> PlaceAsync(OrderRequest request, AccountCredential account);
    Task<OrderResponse> ModifyAsync(string orderId, OrderRequest request, AccountCredential account);
    Task<OrderResponse> CancelAsync(string orderId, AccountCredential account);
    Task SyncOrderBookAsync(AccountCredential account);
    Task<OrderStatus> GetOrderStatusAsync(string orderId, AccountCredential account);
    Task<OrderResponse> ModifyToMarketAsync(string orderId, AccountCredential account);
    IObservable<OrderBook> OrderUpdates { get; }
}

/// <summary>Generic data repository.</summary>
public interface IRepository<T> where T : class
{
    void Upsert(T entity);
    void UpsertAll(IEnumerable<T> entities);
    T? FindById(object id);
    IEnumerable<T> FindAll();
    IEnumerable<T> Find(System.Linq.Expressions.Expression<Func<T, bool>> predicate);
    bool Delete(object id);
    int DeleteMany(System.Linq.Expressions.Expression<Func<T, bool>> predicate);
}

/// <summary>Manages all database operations and repositories.</summary>
public interface IDataBaseManager
{
    void Initialize();
}

/// <summary>Notification service for alerts (Telegram, in-app).</summary>
public interface INotificationService
{
    Task SendMessageAsync(string chatId, string message);
    Task NotifyEntryAsync(StrategyConfig strategy, List<OrderBook> fills, AccountCredential account);
    Task NotifyExitAsync(StrategyConfig strategy, ExitReason reason, decimal mtm, AccountCredential account);
    Task NotifyErrorAsync(string context, Exception ex);
}

/// <summary>Risk management evaluation and enforcement.</summary>
public interface IRiskManager
{
    Task StartAsync();
    Task StopAsync();
    IObservable<RiskEvent> RiskAlerts { get; }
}

/// <summary>Encryption and TOTP utilities.</summary>
public interface IEncryptionService
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
    string GenerateTOTP(string secret);
    bool ValidateTOTP(string secret, string totp);
}

// ─── Supporting models used by interfaces ───

/// <summary>Risk event raised by RiskManager.</summary>
public record RiskEvent(
    RiskEventType Type,
    string StrategyId,
    string AccountId,
    decimal TriggerValue,
    decimal CurrentValue,
    DateTime Timestamp
);

public enum RiskEventType
{
    MaxLoss,
    MaxProfit,
    ProfitLock,
    TimeBased,
    UnderlyingSL,
    Manual
}

/// <summary>Strategy-level event for the event bus.</summary>
public record StrategyEvent(
    string StrategyId,
    StrategyEventType EventType,
    DateTime Timestamp,
    string Details
);

public enum StrategyEventType
{
    EntryPlaced,
    EntryFilled,
    SLHit,
    TargetHit,
    ExitPlaced,
    ExitFilled,
    ReEntry,
    Error,
    Other
}

/// <summary>Summary of positions for a strategy.</summary>
public class PositionSummary
{
    public string StrategyId { get; set; } = string.Empty;
    public decimal TotalMTM { get; set; }
    public decimal RealizedPnL { get; set; }
    public decimal UnrealizedPnL { get; set; }
    public List<Position> OpenLegs { get; set; } = new();
    public List<Position> ClosedLegs { get; set; } = new();
}

/// <summary>MTM summary for an account across all strategies.</summary>
public class AccountMTMSummary
{
    public string AccountId { get; set; } = string.Empty;
    public decimal TotalMTM { get; set; }
    public decimal DayMTM { get; set; }
    public decimal MaxDrawdown { get; set; }
}

/// <summary>In-app alert event.</summary>
public record AlertEvent(
    AlertSeverity Severity,
    string Title,
    string Message,
    DateTime Timestamp
);

public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}
