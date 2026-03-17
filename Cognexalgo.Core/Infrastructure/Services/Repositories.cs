using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Cognexalgo.Core.Application.Interfaces;
using Cognexalgo.Core.Domain.Entities;
using Cognexalgo.Core.Domain.Enums;
using Cognexalgo.Core.Infrastructure.Persistence;

namespace Cognexalgo.Core.Infrastructure.Services
{
    // ═══════════════════════════════════════════════════════════════
    // Generic Repository Base
    // ═══════════════════════════════════════════════════════════════
    public abstract class RepositoryBase<T> : IRepository<T> where T : class
    {
        protected readonly IDbContextFactory<AppDbContext> _factory;

        // Belt-and-suspenders: serialise writes even though each call already
        // gets its own DbContext via the factory (Option A + Option B combined).
        private readonly SemaphoreSlim _dbLock = new SemaphoreSlim(1, 1);

        protected RepositoryBase(IDbContextFactory<AppDbContext> factory) { _factory = factory; }

        public virtual async Task<T?> GetByIdAsync(string id)
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Set<T>().FindAsync(id);
        }

        public virtual async Task<List<T>> GetAllAsync()
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Set<T>().ToListAsync();
        }

        public virtual async Task AddAsync(T entity)
        {
            await _dbLock.WaitAsync();
            try
            {
                await using var db = await _factory.CreateDbContextAsync();
                await db.Set<T>().AddAsync(entity);
                await db.SaveChangesAsync();
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public virtual async Task UpdateAsync(T entity)
        {
            await _dbLock.WaitAsync();
            try
            {
                await using var db = await _factory.CreateDbContextAsync();
                db.Set<T>().Update(entity);
                await db.SaveChangesAsync();
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public virtual async Task DeleteAsync(string id)
        {
            await _dbLock.WaitAsync();
            try
            {
                await using var db = await _factory.CreateDbContextAsync();
                var entity = await db.Set<T>().FindAsync(id);
                if (entity != null)
                {
                    db.Set<T>().Remove(entity);
                    await db.SaveChangesAsync();
                }
            }
            finally
            {
                _dbLock.Release();
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Strategy Repository
    // ═══════════════════════════════════════════════════════════════
    public class StrategyRepository : RepositoryBase<Strategy>, IStrategyRepository
    {
        public StrategyRepository(IDbContextFactory<AppDbContext> factory) : base(factory) { }

        public async Task<List<Strategy>> GetActiveAsync()
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Strategies
                .Include(s => s.Legs)
                .Where(s => s.Status == StrategyStatus.Active)
                .ToListAsync();
        }

        public async Task<List<Strategy>> GetByStatusAsync(StrategyStatus status)
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Strategies
                .Include(s => s.Legs)
                .Where(s => s.Status == status)
                .ToListAsync();
        }

        public async Task<List<Strategy>> GetByDateAsync(DateTime date)
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Strategies
                .Include(s => s.Legs)
                .Where(s => s.CreatedAt.Date == date.Date)
                .ToListAsync();
        }

        public async Task<List<Strategy>> GetTemplatesAsync()
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Strategies
                .Where(s => s.IsTemplate)
                .ToListAsync();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Order Repository
    // ═══════════════════════════════════════════════════════════════
    public class OrderRepository : RepositoryBase<Order>, IOrderRepository
    {
        public OrderRepository(IDbContextFactory<AppDbContext> factory) : base(factory) { }

        public async Task<List<Order>> GetByStrategyAsync(string strategyId)
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Orders
                .Where(o => o.StrategyId == strategyId)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<Order>> GetByStatusAsync(OrderStatus status)
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Orders
                .Where(o => o.Status == status)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<Order>> GetTodayOrdersAsync()
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Orders
                .Where(o => o.CreatedAt.Date == DateTime.UtcNow.Date)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<Order>> GetByDateRangeAsync(DateTime from, DateTime to)
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Orders
                .Where(o => o.CreatedAt >= from && o.CreatedAt <= to)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Signal Repository
    // ═══════════════════════════════════════════════════════════════
    public class SignalRepository : RepositoryBase<Signal>, ISignalRepository
    {
        public SignalRepository(IDbContextFactory<AppDbContext> factory) : base(factory) { }

        public async Task<List<Signal>> GetByStrategyAsync(string strategyId)
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Signals
                .Where(s => s.StrategyId == strategyId)
                .OrderByDescending(s => s.Timestamp)
                .ToListAsync();
        }

        public async Task<Signal?> GetLatestByStrategyAsync(string strategyId)
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Signals
                .Where(s => s.StrategyId == strategyId)
                .OrderByDescending(s => s.Timestamp)
                .FirstOrDefaultAsync();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Trade Repository
    // ═══════════════════════════════════════════════════════════════
    public class TradeRepository : RepositoryBase<Trade>, ITradeRepository
    {
        public TradeRepository(IDbContextFactory<AppDbContext> factory) : base(factory) { }

        public async Task<List<Trade>> GetByStrategyAsync(string strategyId)
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Trades
                .Where(t => t.StrategyId == strategyId)
                .OrderByDescending(t => t.EntryTime)
                .ToListAsync();
        }

        public async Task<decimal> GetTotalPnlByStrategyAsync(string strategyId)
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Trades
                .Where(t => t.StrategyId == strategyId)
                .SumAsync(t => t.NetPnl);
        }

        public async Task<DailyPnlSummary> GetDailySummaryAsync(string strategyId, DateTime date)
        {
            await using var db = await _factory.CreateDbContextAsync();
            var trades = await db.Trades
                .Where(t => t.StrategyId == strategyId && t.EntryTime.Date == date.Date)
                .ToListAsync();

            return new DailyPnlSummary
            {
                Date = date,
                StrategyId = strategyId,
                TotalPnl = trades.Sum(t => t.NetPnl),
                TotalTrades = trades.Count,
                WinCount = trades.Count(t => t.NetPnl > 0),
                LossCount = trades.Count(t => t.NetPnl < 0),
                MaxDrawdown = 0 // TODO: Implement proper drawdown calculation
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Instrument Repository
    // ═══════════════════════════════════════════════════════════════
    public class InstrumentRepository : IInstrumentRepository
    {
        private readonly IDbContextFactory<AppDbContext> _factory;

        public InstrumentRepository(IDbContextFactory<AppDbContext> factory) { _factory = factory; }

        public async Task<InstrumentMaster?> GetByTokenAsync(string token)
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.InstrumentMasters.FindAsync(token);
        }

        public async Task<InstrumentMaster?> GetBySymbolAsync(string symbol)
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.InstrumentMasters.FirstOrDefaultAsync(i => i.Symbol == symbol);
        }

        public async Task<List<InstrumentMaster>> GetOptionChainAsync(string underlying, DateTime? expiry = null)
        {
            await using var db = await _factory.CreateDbContextAsync();
            var query = db.InstrumentMasters
                .Where(i => i.Symbol.Contains(underlying)
                    && (i.InstrumentType == InstrumentType.OPTIDX || i.InstrumentType == InstrumentType.OPTSTK));

            if (expiry.HasValue)
                query = query.Where(i => i.Expiry == expiry.Value.Date);

            return await query.OrderBy(i => i.StrikePrice).ThenBy(i => i.OptionType).ToListAsync();
        }

        public async Task<int> GetLotSizeAsync(string symbol)
        {
            await using var db = await _factory.CreateDbContextAsync();
            var instrument = await db.InstrumentMasters.FirstOrDefaultAsync(i => i.Symbol == symbol);
            return instrument?.LotSize ?? 1;
        }

        public async Task BulkUpsertAsync(List<InstrumentMaster> instruments)
        {
            await using var db = await _factory.CreateDbContextAsync();
            foreach (var inst in instruments)
            {
                var existing = await db.InstrumentMasters.FindAsync(inst.Token);
                if (existing != null)
                {
                    db.Entry(existing).CurrentValues.SetValues(inst);
                }
                else
                {
                    await db.InstrumentMasters.AddAsync(inst);
                }
            }
            await db.SaveChangesAsync();
        }

        public async Task<DateTime?> GetLastSyncTimeAsync()
        {
            await using var db = await _factory.CreateDbContextAsync();
            var latest = await db.InstrumentMasters
                .OrderByDescending(i => i.LastUpdated)
                .FirstOrDefaultAsync();
            return latest?.LastUpdated;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Credential Repository
    // ═══════════════════════════════════════════════════════════════
    public class CredentialRepository : ICredentialRepository
    {
        private readonly IDbContextFactory<AppDbContext> _factory;

        public CredentialRepository(IDbContextFactory<AppDbContext> factory) { _factory = factory; }

        public async Task<BrokerCredential?> GetAsync(string brokerName = "AngelOne")
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.BrokerCredentials.FirstOrDefaultAsync(c => c.BrokerName == brokerName);
        }

        public async Task SaveAsync(BrokerCredential credential)
        {
            await using var db = await _factory.CreateDbContextAsync();
            var existing = await db.BrokerCredentials.FirstOrDefaultAsync(c => c.BrokerName == credential.BrokerName);
            if (existing != null)
            {
                db.Entry(existing).CurrentValues.SetValues(credential);
            }
            else
            {
                await db.BrokerCredentials.AddAsync(credential);
            }
            await db.SaveChangesAsync();
        }
    }
}
