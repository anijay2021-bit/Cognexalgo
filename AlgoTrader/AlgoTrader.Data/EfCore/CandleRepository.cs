using AlgoTrader.Core.Enums;
using AlgoTrader.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlgoTrader.Data.EfCore;

/// <summary>Repository for historical candle data in SQLite.</summary>
public class CandleRepository
{
    private readonly string _folderPath;
    private readonly ILogger<CandleRepository> _logger;

    public CandleRepository(string folderPath, ILogger<CandleRepository> logger)
    {
        _folderPath = folderPath;
        _logger = logger;

        // Ensure database and tables exist
        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();
    }

    private CandleDbContext CreateContext() => new CandleDbContext(_folderPath);

    /// <summary>Upsert candles — avoids duplicates by checking Symbol+Interval+Timestamp.</summary>
    public async Task UpsertCandlesAsync(List<Candle> candles)
    {
        using var ctx = CreateContext();
        foreach (var candle in candles)
        {
            var existing = await ctx.Candles.FirstOrDefaultAsync(c =>
                c.Symbol == candle.Symbol &&
                c.Interval == candle.Interval.ToString() &&
                c.Timestamp == candle.Timestamp);

            if (existing != null)
            {
                existing.Open = candle.Open;
                existing.High = candle.High;
                existing.Low = candle.Low;
                existing.Close = candle.Close;
                existing.Volume = candle.Volume;
                existing.OI = candle.OI;
            }
            else
            {
                ctx.Candles.Add(new CandleEntity
                {
                    Symbol = candle.Symbol,
                    Token = candle.Token,
                    Exchange = "", // populated during fetch
                    Interval = candle.Interval.ToString(),
                    Timestamp = candle.Timestamp,
                    Open = candle.Open,
                    High = candle.High,
                    Low = candle.Low,
                    Close = candle.Close,
                    Volume = candle.Volume,
                    OI = candle.OI
                });
            }
        }
        await ctx.SaveChangesAsync();
        _logger.LogDebug("Upserted {Count} candles for {Symbol}", candles.Count, candles.FirstOrDefault()?.Symbol);
    }

    public async Task<List<Candle>> GetCandlesAsync(string symbol, TimeFrame interval, DateTime from, DateTime to)
    {
        using var ctx = CreateContext();
        var intervalStr = interval.ToString();
        return await ctx.Candles
            .Where(c => c.Symbol == symbol && c.Interval == intervalStr && c.Timestamp >= from && c.Timestamp <= to)
            .OrderBy(c => c.Timestamp)
            .Select(c => new Candle
            {
                Symbol = c.Symbol,
                Token = c.Token,
                Interval = interval,
                Open = c.Open, High = c.High, Low = c.Low, Close = c.Close,
                Volume = c.Volume, OI = c.OI, Timestamp = c.Timestamp
            })
            .ToListAsync();
    }

    public async Task<Candle?> GetLastCandleAsync(string symbol, TimeFrame interval)
    {
        using var ctx = CreateContext();
        var intervalStr = interval.ToString();
        var entity = await ctx.Candles
            .Where(c => c.Symbol == symbol && c.Interval == intervalStr)
            .OrderByDescending(c => c.Timestamp)
            .FirstOrDefaultAsync();

        if (entity == null) return null;
        return new Candle
        {
            Symbol = entity.Symbol, Token = entity.Token, Interval = interval,
            Open = entity.Open, High = entity.High, Low = entity.Low, Close = entity.Close,
            Volume = entity.Volume, OI = entity.OI, Timestamp = entity.Timestamp
        };
    }

    public async Task<int> DeleteOlderThanAsync(DateTime cutoff)
    {
        using var ctx = CreateContext();
        var old = ctx.Candles.Where(c => c.Timestamp < cutoff);
        ctx.Candles.RemoveRange(old);
        return await ctx.SaveChangesAsync();
    }
}
