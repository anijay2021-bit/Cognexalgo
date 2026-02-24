using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cognexalgo.Core.Domain.Entities;
using Cognexalgo.Core.Domain.Enums;

namespace Cognexalgo.Core.Application.Interfaces
{
    /// <summary>Generic repository pattern for all entities.</summary>
    public interface IRepository<T> where T : class
    {
        Task<T?> GetByIdAsync(string id);
        Task<List<T>> GetAllAsync();
        Task AddAsync(T entity);
        Task UpdateAsync(T entity);
        Task DeleteAsync(string id);
    }

    /// <summary>Strategy-specific repository with query methods.</summary>
    public interface IStrategyRepository : IRepository<Strategy>
    {
        Task<List<Strategy>> GetActiveAsync();
        Task<List<Strategy>> GetByStatusAsync(StrategyStatus status);
        Task<List<Strategy>> GetByDateAsync(DateTime date);
        Task<List<Strategy>> GetTemplatesAsync();
    }

    /// <summary>Order-specific repository with query methods.</summary>
    public interface IOrderRepository : IRepository<Order>
    {
        Task<List<Order>> GetByStrategyAsync(string strategyId);
        Task<List<Order>> GetByStatusAsync(OrderStatus status);
        Task<List<Order>> GetTodayOrdersAsync();
        Task<List<Order>> GetByDateRangeAsync(DateTime from, DateTime to);
    }

    /// <summary>Signal-specific repository.</summary>
    public interface ISignalRepository : IRepository<Signal>
    {
        Task<List<Signal>> GetByStrategyAsync(string strategyId);
        Task<Signal?> GetLatestByStrategyAsync(string strategyId);
    }

    /// <summary>Trade-specific repository for analytics.</summary>
    public interface ITradeRepository : IRepository<Trade>
    {
        Task<List<Trade>> GetByStrategyAsync(string strategyId);
        Task<decimal> GetTotalPnlByStrategyAsync(string strategyId);
        Task<DailyPnlSummary> GetDailySummaryAsync(string strategyId, DateTime date);
    }

    /// <summary>Instrument master cache operations.</summary>
    public interface IInstrumentRepository
    {
        Task<InstrumentMaster?> GetByTokenAsync(string token);
        Task<InstrumentMaster?> GetBySymbolAsync(string symbol);
        Task<List<InstrumentMaster>> GetOptionChainAsync(string underlying, DateTime? expiry = null);
        Task<int> GetLotSizeAsync(string symbol);
        Task BulkUpsertAsync(List<InstrumentMaster> instruments);
        Task<DateTime?> GetLastSyncTimeAsync();
    }

    /// <summary>Credential storage with encryption.</summary>
    public interface ICredentialRepository
    {
        Task<BrokerCredential?> GetAsync(string brokerName = "AngelOne");
        Task SaveAsync(BrokerCredential credential);
    }
}
