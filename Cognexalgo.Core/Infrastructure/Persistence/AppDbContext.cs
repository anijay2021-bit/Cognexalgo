using Microsoft.EntityFrameworkCore;
using Cognexalgo.Core.Domain.Entities;

namespace Cognexalgo.Core.Infrastructure.Persistence
{
    /// <summary>
    /// EF Core DbContext for Cognexalgo V2.
    /// Supports Supabase (PostgreSQL) as primary store with SQLite fallback.
    /// </summary>
    public class AppDbContext : DbContext
    {
        // ─── Core Trading Entities ───────────────────────────────
        public DbSet<Strategy> Strategies { get; set; } = null!;
        public DbSet<StrategyLeg> StrategyLegs { get; set; } = null!;
        public DbSet<Order> Orders { get; set; } = null!;
        public DbSet<Signal> Signals { get; set; } = null!;
        public DbSet<Trade> Trades { get; set; } = null!;

        // ─── Supporting Entities ─────────────────────────────────
        public DbSet<RmsLog> RmsLogs { get; set; } = null!;
        public DbSet<EventLog> EventLogs { get; set; } = null!;
        public DbSet<InstrumentMaster> InstrumentMasters { get; set; } = null!;
        public DbSet<DailyPnlSummary> DailyPnlSummaries { get; set; } = null!;
        public DbSet<Candle> Candles { get; set; } = null!;
        public DbSet<Tick> Ticks { get; set; } = null!;
        public DbSet<BrokerCredential> BrokerCredentials { get; set; } = null!;

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ─── Strategy ────────────────────────────────────────
            modelBuilder.Entity<Strategy>(entity =>
            {
                entity.HasKey(e => e.StrategyId);
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.CreatedAt);
                entity.HasIndex(e => e.TradingMode);

                entity.HasMany(e => e.Legs)
                      .WithOne(l => l.Strategy)
                      .HasForeignKey(l => l.StrategyId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(e => e.Orders)
                      .WithOne(o => o.Strategy)
                      .HasForeignKey(o => o.StrategyId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasMany(e => e.Signals)
                      .WithOne(s => s.Strategy)
                      .HasForeignKey(s => s.StrategyId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasMany(e => e.Trades)
                      .WithOne(t => t.Strategy)
                      .HasForeignKey(t => t.StrategyId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── StrategyLeg ─────────────────────────────────────
            modelBuilder.Entity<StrategyLeg>(entity =>
            {
                entity.HasKey(e => e.LegId);
                entity.HasIndex(e => e.StrategyId);
                entity.HasIndex(e => e.Status);
            });

            // ─── Order ───────────────────────────────────────────
            modelBuilder.Entity<Order>(entity =>
            {
                entity.HasKey(e => e.OrderId);
                entity.HasIndex(e => e.StrategyId);
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.CreatedAt);
                entity.HasIndex(e => e.TradingMode);
                entity.HasIndex(e => e.BrokerOrderId);
            });

            // ─── Signal ─────────────────────────────────────────
            modelBuilder.Entity<Signal>(entity =>
            {
                entity.HasKey(e => e.SignalId);
                entity.HasIndex(e => e.StrategyId);
                entity.HasIndex(e => e.Timestamp);
            });

            // ─── Trade ──────────────────────────────────────────
            modelBuilder.Entity<Trade>(entity =>
            {
                entity.HasKey(e => e.TradeId);
                entity.HasIndex(e => e.StrategyId);
                entity.HasIndex(e => e.EntryTime);
            });

            // ─── InstrumentMaster ─────────────────────────────────
            modelBuilder.Entity<InstrumentMaster>(entity =>
            {
                entity.HasKey(e => e.Token);
                entity.HasIndex(e => e.Symbol);
                entity.HasIndex(e => e.Exchange);
                entity.HasIndex(e => e.Expiry);
            });

            // ─── DailyPnlSummary ────────────────────────────────
            modelBuilder.Entity<DailyPnlSummary>(entity =>
            {
                entity.HasIndex(e => new { e.Date, e.StrategyId }).IsUnique();
            });

            // ─── Candle ─────────────────────────────────────────
            modelBuilder.Entity<Candle>(entity =>
            {
                entity.HasIndex(e => new { e.Symbol, e.TimeFrame, e.Timestamp });
            });

            // ─── Tick ───────────────────────────────────────────
            modelBuilder.Entity<Tick>(entity =>
            {
                entity.HasIndex(e => new { e.Symbol, e.Timestamp });
            });

            // Store enums as strings for readability in PostgreSQL
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.ClrType.IsEnum)
                    {
                        property.SetProviderClrType(typeof(string));
                    }
                }
            }
        }
    }
}
