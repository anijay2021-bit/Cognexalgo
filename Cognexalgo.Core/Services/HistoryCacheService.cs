using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Cognexalgo.Core.Data;
using Cognexalgo.Core.Data.Entities;
using Skender.Stock.Indicators;

namespace Cognexalgo.Core.Services
{
    public class HistoryCacheService
    {
        private readonly HistoryCacheContext _context;

        public HistoryCacheService()
        {
            _context = new HistoryCacheContext();
            _context.Database.EnsureCreated();
        }

        public async Task<List<Quote>> GetHistoryAsync(string symbol, string interval, int days)
        {
            DateTime cutoff = DateTime.Now.AddDays(-days);
            
            var candles = await _context.CachedCandles
                .Where(c => c.Symbol == symbol && c.Interval == interval && c.Timestamp >= cutoff)
                .OrderBy(c => c.Timestamp)
                .AsNoTracking()
                .ToListAsync();

            return candles.Select(c => new Quote
            {
                Date = c.Timestamp,
                Open = c.Open,
                High = c.High,
                Low = c.Low,
                Close = c.Close,
                Volume = c.Volume
            }).ToList();
        }

        public async Task SaveHistoryAsync(string symbol, string interval, List<Quote> quotes)
        {
            if (quotes == null || !quotes.Any()) return;

            // Use a transaction for performance
            using (var transaction = await _context.Database.BeginTransactionAsync())
            {
                try
                {
                    foreach (var q in quotes)
                    {
                        // Upsert logic (manually for SQLite compatibility in EF)
                        var existing = await _context.CachedCandles
                            .FirstOrDefaultAsync(c => c.Symbol == symbol && c.Interval == interval && c.Timestamp == q.Date);

                        if (existing != null)
                        {
                            existing.Open = q.Open;
                            existing.High = q.High;
                            existing.Low = q.Low;
                            existing.Close = q.Close;
                            existing.Volume = q.Volume;
                            existing.LastUpdated = DateTime.Now;
                        }
                        else
                        {
                            _context.CachedCandles.Add(new CachedCandle
                            {
                                Symbol = symbol,
                                Interval = interval,
                                Timestamp = q.Date,
                                Open = q.Open,
                                High = q.High,
                                Low = q.Low,
                                Close = q.Close,
                                Volume = q.Volume,
                                LastUpdated = DateTime.Now
                            });
                        }
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
        }

        /// <summary>
        /// Purges old data beyond 30 days to keep the DB size manageable.
        /// </summary>
        public async Task PurgeOldDataAsync(int retentionDays = 30)
        {
            DateTime cutoff = DateTime.Now.AddDays(-retentionDays);
            var old = _context.CachedCandles.Where(c => c.Timestamp < cutoff);
            _context.CachedCandles.RemoveRange(old);
            await _context.SaveChangesAsync();
        }
    }
}
