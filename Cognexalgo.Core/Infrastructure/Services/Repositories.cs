using System;
using System.Collections.Generic;
using System.Linq;
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
        protected readonly AppDbContext Db;

        protected RepositoryBase(AppDbContext db) { Db = db; }

        public virtual async Task<T?> GetByIdAsync(string id)
            => await Db.Set<T>().FindAsync(id);

        public virtual async Task<List<T>> GetAllAsync()
            => await Db.Set<T>().ToListAsync();

        public virtual async Task AddAsync(T entity)
        {
            await Db.Set<T>().AddAsync(entity);
            await Db.SaveChangesAsync();
        }

        public virtual async Task UpdateAsync(T entity)
        {
            Db.Set<T>().Update(entity);
            await Db.SaveChangesAsync();
        }

        public virtual async Task DeleteAsync(string id)
        {
            var entity = await GetByIdAsync(id);
            if (entity != null)
            {
                Db.Set<T>().Remove(entity);
                await Db.SaveChangesAsync();
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Strategy Repository
    // ═══════════════════════════════════════════════════════════════
    public class StrategyRepository : RepositoryBase<Strategy>, IStrategyRepository
    {
        public StrategyRepository(AppDbContext db) : base(db) { }

        public async Task<List<Strategy>> GetActiveAsync()
            => await Db.Strategies
                .Include(s => s.Legs)
                .Where(s => s.Status == StrategyStatus.Active)
                .ToListAsync();

        public async Task<List<Strategy>> GetByStatusAsync(StrategyStatus status)
            => await Db.Strategies
                .Include(s => s.Legs)
                .Where(s => s.Status == status)
                .ToListAsync();

        public async Task<List<Strategy>> GetByDateAsync(DateTime date)
            => await Db.Strategies
                .Include(s => s.Legs)
                .Where(s => s.CreatedAt.Date == date.Date)
                .ToListAsync();

        public async Task<List<Strategy>> GetTemplatesAsync()
            => await Db.Strategies
                .Where(s => s.IsTemplate)
                .ToListAsync();
    }

    // ═══════════════════════════════════════════════════════════════
    // Order Repository
    // ═══════════════════════════════════════════════════════════════
    public class OrderRepository : RepositoryBase<Order>, IOrderRepository
    {
        public OrderRepository(AppDbContext db) : base(db) { }

        public async Task<List<Order>> GetByStrategyAsync(string strategyId)
            => await Db.Orders
                .Where(o => o.StrategyId == strategyId)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

        public async Task<List<Order>> GetByStatusAsync(OrderStatus status)
            => await Db.Orders
                .Where(o => o.Status == status)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

        public async Task<List<Order>> GetTodayOrdersAsync()
            => await Db.Orders
                .Where(o => o.CreatedAt.Date == DateTime.UtcNow.Date)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

        public async Task<List<Order>> GetByDateRangeAsync(DateTime from, DateTime to)
            => await Db.Orders
                .Where(o => o.CreatedAt >= from && o.CreatedAt <= to)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();
    }

    // ═══════════════════════════════════════════════════════════════
    // Signal Repository
    // ═══════════════════════════════════════════════════════════════
    public class SignalRepository : RepositoryBase<Signal>, ISignalRepository
    {
        public SignalRepository(AppDbContext db) : base(db) { }

        public async Task<List<Signal>> GetByStrategyAsync(string strategyId)
            => await Db.Signals
                .Where(s => s.StrategyId == strategyId)
                .OrderByDescending(s => s.Timestamp)
                .ToListAsync();

        public async Task<Signal?> GetLatestByStrategyAsync(string strategyId)
            => await Db.Signals
                .Where(s => s.StrategyId == strategyId)
                .OrderByDescending(s => s.Timestamp)
                .FirstOrDefaultAsync();
    }

    // ═══════════════════════════════════════════════════════════════
    // Trade Repository
    // ═══════════════════════════════════════════════════════════════
    public class TradeRepository : RepositoryBase<Trade>, ITradeRepository
    {
        public TradeRepository(AppDbContext db) : base(db) { }

        public async Task<List<Trade>> GetByStrategyAsync(string strategyId)
            => await Db.Trades
                .Where(t => t.StrategyId == strategyId)
                .OrderByDescending(t => t.EntryTime)
                .ToListAsync();

        public async Task<decimal> GetTotalPnlByStrategyAsync(string strategyId)
            => await Db.Trades
                .Where(t => t.StrategyId == strategyId)
                .SumAsync(t => t.NetPnl);

        public async Task<DailyPnlSummary> GetDailySummaryAsync(string strategyId, DateTime date)
        {
            var trades = await Db.Trades
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
        private readonly AppDbContext _db;

        public InstrumentRepository(AppDbContext db) { _db = db; }

        public async Task<InstrumentMaster?> GetByTokenAsync(string token)
            => await _db.InstrumentMasters.FindAsync(token);

        public async Task<InstrumentMaster?> GetBySymbolAsync(string symbol)
            => await _db.InstrumentMasters.FirstOrDefaultAsync(i => i.Symbol == symbol);

        public async Task<List<InstrumentMaster>> GetOptionChainAsync(string underlying, DateTime? expiry = null)
        {
            var query = _db.InstrumentMasters
                .Where(i => i.Symbol.Contains(underlying)
                    && (i.InstrumentType == InstrumentType.OPTIDX || i.InstrumentType == InstrumentType.OPTSTK));

            if (expiry.HasValue)
                query = query.Where(i => i.Expiry == expiry.Value.Date);

            return await query.OrderBy(i => i.StrikePrice).ThenBy(i => i.OptionType).ToListAsync();
        }

        public async Task<int> GetLotSizeAsync(string symbol)
        {
            var instrument = await GetBySymbolAsync(symbol);
            return instrument?.LotSize ?? 1;
        }

        public async Task BulkUpsertAsync(List<InstrumentMaster> instruments)
        {
            foreach (var inst in instruments)
            {
                var existing = await _db.InstrumentMasters.FindAsync(inst.Token);
                if (existing != null)
                {
                    _db.Entry(existing).CurrentValues.SetValues(inst);
                }
                else
                {
                    await _db.InstrumentMasters.AddAsync(inst);
                }
            }
            await _db.SaveChangesAsync();
        }

        public async Task<DateTime?> GetLastSyncTimeAsync()
        {
            var latest = await _db.InstrumentMasters
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
        private readonly AppDbContext _db;

        public CredentialRepository(AppDbContext db) { _db = db; }

        public async Task<BrokerCredential?> GetAsync(string brokerName = "AngelOne")
            => await _db.BrokerCredentials.FirstOrDefaultAsync(c => c.BrokerName == brokerName);

        public async Task SaveAsync(BrokerCredential credential)
        {
            var existing = await GetAsync(credential.BrokerName);
            if (existing != null)
            {
                _db.Entry(existing).CurrentValues.SetValues(credential);
            }
            else
            {
                await _db.BrokerCredentials.AddAsync(credential);
            }
            await _db.SaveChangesAsync();
        }
    }
}
