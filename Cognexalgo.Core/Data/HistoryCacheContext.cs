using System;
using Microsoft.EntityFrameworkCore;
using Cognexalgo.Core.Data.Entities;
using System.IO;

namespace Cognexalgo.Core.Data
{
    public class HistoryCacheContext : DbContext
    {
        public DbSet<CachedCandle> CachedCandles { get; set; }

        private string _dbPath;

        public HistoryCacheContext()
        {
            var folder = AppContext.BaseDirectory;
            _dbPath = Path.Combine(folder, "LocalHistoryCache.db");
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite($"Data Source={_dbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CachedCandle>(entity =>
            {
                entity.ToTable("cached_candles");
                entity.HasKey(e => e.Id);
                
                // Index for fast lookups by segment
                entity.HasIndex(e => new { e.Symbol, e.Interval, e.Timestamp }).IsUnique();
                
                // Efficient querying for range delete
                entity.HasIndex(e => e.Timestamp);
            });
        }
    }
}
