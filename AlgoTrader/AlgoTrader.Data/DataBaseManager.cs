using AlgoTrader.Core.Interfaces;
using AlgoTrader.Core.Models;
using AlgoTrader.Data.LiteDb;
using AlgoTrader.Data.Repositories;
using Microsoft.Extensions.Logging;

namespace AlgoTrader.Data;

/// <summary>Manages all database operations, wires up repositories, and initialises storage on startup.</summary>
public class DataBaseManager : IDataBaseManager
{
    private readonly LiteDbContext _liteDbContext;
    private readonly ILogger<DataBaseManager> _logger;

    public AccountRepository Accounts { get; }
    public StrategyRepository Strategies { get; }
    public OrderRepository Orders { get; }
    public SettingsRepository Settings { get; }

    public DataBaseManager(LiteDbContext liteDbContext, ILogger<DataBaseManager> logger)
    {
        _liteDbContext = liteDbContext;
        _logger = logger;

        Accounts = new AccountRepository(liteDbContext);
        Strategies = new StrategyRepository(liteDbContext);
        Orders = new OrderRepository(liteDbContext);
        Settings = new SettingsRepository(liteDbContext);
    }

    public void Initialize()
    {
        _logger.LogInformation("DataBaseManager initialised — LiteDB repositories ready");
    }
}
