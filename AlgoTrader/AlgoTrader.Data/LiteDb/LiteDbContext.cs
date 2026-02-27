using LiteDB;
using AlgoTrader.Core.Models;
using Microsoft.Extensions.Logging;

namespace AlgoTrader.Data.LiteDb;

/// <summary>LiteDB database context — manages the single DB connection and collection access.</summary>
public class LiteDbContext : IDisposable
{
    private readonly LiteDatabase _db;
    private readonly ILogger<LiteDbContext> _logger;

    public LiteDbContext(DatabaseConfig config, ILogger<LiteDbContext> logger)
    {
        _logger = logger;

        if (!Directory.Exists(config.FolderPath))
        {
            Directory.CreateDirectory(config.FolderPath);
            _logger.LogInformation("Created database folder: {FolderPath}", config.FolderPath);
        }

        var mapper = BsonMapper.Global;
        
        // Handle cases where IDs might be stored as ObjectId but expected as string
        mapper.RegisterType<string>(
            s => s,
            v => v.IsObjectId ? v.AsObjectId.ToString() : v.AsString
        );

        mapper.Entity<AccountCredential>()
            .Id(x => x.ClientID, false); // ClientID is the PK, no auto-gen

        _db = new LiteDatabase(config.FullPath, mapper);
        _logger.LogInformation("LiteDB opened: {FullPath}", config.FullPath);

        EnsureIndexes();
    }

    /// <summary>Get a typed collection from the database.</summary>
    public ILiteCollection<T> GetCollection<T>(string? name = null)
        => _db.GetCollection<T>(name ?? typeof(T).Name);

    private void EnsureIndexes()
    {
        // AccountCredential indexes
        var accounts = GetCollection<AccountCredential>();
        accounts.EnsureIndex(x => x.ClientID);
        accounts.EnsureIndex(x => x.BrokerType);
        accounts.EnsureIndex(x => x.GroupName);

        // StrategyConfig indexes
        var strategies = GetCollection<StrategyConfig>();
        strategies.EnsureIndex(x => x.Id);
        strategies.EnsureIndex(x => x.IsActive);
        strategies.EnsureIndex(x => x.PortfolioId);

        // OrderBook indexes
        var orders = GetCollection<AlgoTrader.Core.Models.OrderBook>();
        orders.EnsureIndex(x => x.OrderID);
        orders.EnsureIndex(x => x.Status);
        orders.EnsureIndex(x => x.PlaceTime);

        _logger.LogDebug("LiteDB indexes ensured");
    }

    public void Dispose()
    {
        _db?.Dispose();
        _logger.LogInformation("LiteDB connection closed");
    }
}
