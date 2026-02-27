using AlgoTrader.Core.Enums;
using AlgoTrader.Core.Models;

namespace AlgoTrader.Data.Repositories;

/// <summary>CRUD for broker account credentials.</summary>
public class AccountRepository : LiteDb.LiteRepository<AccountCredential>
{
    public AccountRepository(LiteDb.LiteDbContext context) : base(context, "Accounts") { }

    public IEnumerable<AccountCredential> GetByBroker(BrokerType broker)
        => Find(x => x.BrokerType == broker);

    public IEnumerable<AccountCredential> GetByGroup(string groupName)
        => Find(x => x.GroupName == groupName);

    public AccountCredential? GetByClientId(string clientId)
        => Find(x => x.ClientID == clientId).FirstOrDefault();
}

/// <summary>CRUD for strategy configurations.</summary>
public class StrategyRepository : LiteDb.LiteRepository<StrategyConfig>
{
    public StrategyRepository(LiteDb.LiteDbContext context) : base(context, "Strategies") { }

    public IEnumerable<StrategyConfig> GetActive()
        => Find(x => x.IsActive);

    public IEnumerable<StrategyConfig> GetByPortfolio(string portfolioId)
        => Find(x => x.PortfolioId == portfolioId);

    public StrategyConfig? CloneStrategy(Guid strategyId)
    {
        var original = FindById(strategyId);
        if (original == null) return null;

        var clone = original with {
            Id = Guid.NewGuid(),
            Name = original.Name + " (Copy)",
            State = StrategyState.IDLE,
            CreatedAt = DateTime.UtcNow
        };
        // Deep clone all legs with new IDs
        clone = clone with { Legs = clone.Legs.Select(l => l with { Id = Guid.NewGuid() }).ToList() };
        Upsert(clone);
        return clone;
    }
}

/// <summary>CRUD for order book records.</summary>
public class OrderRepository : LiteDb.LiteRepository<OrderBook>
{
    public OrderRepository(LiteDb.LiteDbContext context) : base(context, "Orders") { }

    public IEnumerable<OrderBook> GetByDateRange(DateTime from, DateTime to)
        => Find(x => x.PlaceTime >= from && x.PlaceTime <= to);

    public IEnumerable<OrderBook> GetByStatus(OrderStatus status)
        => Find(x => x.Status == status);
}

/// <summary>Key-value settings store.</summary>
public class SettingsRepository
{
    private readonly LiteDb.LiteDbContext _context;

    public SettingsRepository(LiteDb.LiteDbContext context)
    {
        _context = context;
    }

    public T? Get<T>(string key)
    {
        var col = _context.GetCollection<SettingEntry>("Settings");
        var entry = col.FindById(key);
        if (entry == null) return default;
        return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(entry.JsonValue);
    }

    public void Set<T>(string key, T value)
    {
        var col = _context.GetCollection<SettingEntry>("Settings");
        col.Upsert(new SettingEntry
        {
            Key = key,
            JsonValue = Newtonsoft.Json.JsonConvert.SerializeObject(value),
            UpdatedAt = DateTime.UtcNow
        });
    }
}

/// <summary>Internal key-value entry for settings.</summary>
public class SettingEntry
{
    [LiteDB.BsonId]
    public string Key { get; set; } = string.Empty;
    public string JsonValue { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
