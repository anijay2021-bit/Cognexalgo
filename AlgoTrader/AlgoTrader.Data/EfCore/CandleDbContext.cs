using Microsoft.EntityFrameworkCore;

namespace AlgoTrader.Data.EfCore;

/// <summary>EF Core context for historical candle storage in SQLite.</summary>
public class CandleDbContext : DbContext
{
    private readonly string _connectionString;

    public DbSet<CandleEntity> Candles { get; set; } = null!;

    public CandleDbContext(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);

        _connectionString = $"Data Source={Path.Combine(folderPath, "candles.db")}";
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite(_connectionString);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CandleEntity>(entity =>
        {
            entity.ToTable("Candles");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.HasIndex(e => new { e.Symbol, e.Interval, e.Timestamp })
                  .HasDatabaseName("IX_Candles_Symbol_Interval_Timestamp");
            entity.HasIndex(e => e.Symbol);
            entity.HasIndex(e => e.Timestamp);
        });
    }
}

/// <summary>Candle entity for SQLite storage.</summary>
public class CandleEntity
{
    public int Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public string Interval { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }
    public long OI { get; set; }
}

/// <summary>Design-time factory for EF Core migrations.</summary>
public class CandleDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<CandleDbContext>
{
    public CandleDbContext CreateDbContext(string[] args)
    {
        return new CandleDbContext(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AlgoTrader"));
    }
}
