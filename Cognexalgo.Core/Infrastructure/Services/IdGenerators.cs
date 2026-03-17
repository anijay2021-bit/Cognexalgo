using System;
using System.Linq;
using System.Threading.Tasks;
using Cognexalgo.Core.Domain.Enums;
using Cognexalgo.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cognexalgo.Core.Infrastructure.Services
{
    /// <summary>
    /// Generates deterministic Strategy IDs.
    /// Format: STR-{YYYYMMDD}-{TYPE}-{SEQ:000}
    /// Examples: STR-20260128-STRD-001, STR-20260128-CSTM-002
    /// </summary>
    public interface IStrategyIdGenerator
    {
        Task<string> GenerateAsync(StrategyType type);
    }

    public class StrategyIdGenerator : IStrategyIdGenerator
    {
        private readonly IDbContextFactory<AppDbContext> _factory;

        public StrategyIdGenerator(IDbContextFactory<AppDbContext> factory)
        {
            _factory = factory;
        }

        public async Task<string> GenerateAsync(StrategyType type)
        {
            string dateStr = DateTime.Now.ToString("yyyyMMdd");
            string typeCode = type.ToString(); // STRD, STNG, CNDL, CSTM, PRBL, SCPT

            string prefix = $"STR-{dateStr}-";
            await using var db = await _factory.CreateDbContextAsync();
            int todayCount = await db.Strategies
                .CountAsync(s => s.StrategyId.StartsWith(prefix));

            int seq = todayCount + 1;
            return $"STR-{dateStr}-{typeCode}-{seq:D3}";
        }
    }

    /// <summary>
    /// Generates deterministic Order IDs.
    /// Format: ORD-{StrategyId_condensed}-L{LegNum}-{HHmmss}-{SEQ:000}
    /// Example: ORD-STR20260128STRD001-L1-152601-001
    /// </summary>
    public interface IOrderIdGenerator
    {
        Task<string> GenerateAsync(string strategyId, int legNumber);
    }

    public class OrderIdGenerator : IOrderIdGenerator
    {
        private readonly IDbContextFactory<AppDbContext> _factory;

        public OrderIdGenerator(IDbContextFactory<AppDbContext> factory)
        {
            _factory = factory;
        }

        public async Task<string> GenerateAsync(string strategyId, int legNumber)
        {
            string condensedStratId = strategyId.Replace("-", "");
            string timeStr = DateTime.Now.ToString("HHmmss");
            string prefix = $"ORD-{condensedStratId}-L{legNumber}-{timeStr}-";

            await using var db = await _factory.CreateDbContextAsync();
            int count = await db.Orders
                .CountAsync(o => o.OrderId.StartsWith(prefix));

            int seq = count + 1;
            return $"ORD-{condensedStratId}-L{legNumber}-{timeStr}-{seq:D3}";
        }
    }

    /// <summary>
    /// Generates Leg IDs.
    /// Format: LEG-{StrategyId}-{LegNumber:00}
    /// Example: LEG-STR-20260128-STRD-001-01
    /// </summary>
    public interface ILegIdGenerator
    {
        string Generate(string strategyId, int legNumber);
    }

    public class LegIdGenerator : ILegIdGenerator
    {
        public string Generate(string strategyId, int legNumber)
        {
            return $"LEG-{strategyId}-{legNumber:D2}";
        }
    }
}
