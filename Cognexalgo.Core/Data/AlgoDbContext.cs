using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Cognexalgo.Core.Data.Entities;

using Cognexalgo.Core.Models;

namespace Cognexalgo.Core.Data
{
    public class AlgoDbContext : DbContext
    {
        public DbSet<HybridStrategyEntity> HybridStrategies { get; set; }
        public DbSet<StrategyExecutionLog> ExecutionLogs { get; set; }
        public DbSet<AccountConfig> AccountConfigs { get; set; }
        public DbSet<ActivePosition> ActivePositions { get; set; }
        
        // [NEW] Safe-Exit Protocol
        public DbSet<ClientSession> ClientSessions { get; set; }
        public DbSet<TradeHistory> TradeHistory { get; set; }
        
        private readonly IConfiguration _configuration;

        public AlgoDbContext()
        {
            // Constructor for design-time support
        }

        public AlgoDbContext(DbContextOptions<AlgoDbContext> options) : base(options)
        {
        }

        public AlgoDbContext(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                string connectionString = _configuration?.GetConnectionString("AlgoDatabase");
                
                // Fallback for design-time or direct instantiation only if config is missing
                if (string.IsNullOrEmpty(connectionString))
                {
                    // This is a placeholder or default if not provided via DI
                    // Ideally, should always use DI or provided options
                    connectionString = "Host=localhost;Database=algopro;Username=postgres;Password=password";
                }
                
                optionsBuilder.UseNpgsql(connectionString);
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<HybridStrategyEntity>(entity =>
            {
                entity.ToTable("hybrid_strategies");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.HasIndex(e => e.Name).IsUnique();
                entity.HasIndex(e => e.IsActive);
            });
            
            modelBuilder.Entity<StrategyExecutionLog>(entity =>
            {
                entity.ToTable("strategy_execution_logs");
                entity.HasKey(e => e.Id);
                /*
                entity.HasOne(e => e.Strategy)
                      .WithMany(s => s.ExecutionLogs)
                      .HasForeignKey(e => e.StrategyId)
                      .OnDelete(DeleteBehavior.Cascade);
                */
            });
        }
    }
}
