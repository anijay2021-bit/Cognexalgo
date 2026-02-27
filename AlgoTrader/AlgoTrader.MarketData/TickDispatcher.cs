using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using AlgoTrader.Core.Interfaces;
using AlgoTrader.Core.Models;
using Microsoft.Extensions.Logging;

namespace AlgoTrader.MarketData;

/// <summary>
/// Central tick dispatcher — subscribes to the raw feed, maintains latest ticks,
/// provides per-token filtered observables throttled for UI (4fps).
/// </summary>
public class TickDispatcher : IDisposable
{
    private readonly IMarketDataService _marketData;
    private readonly ILogger<TickDispatcher> _logger;
    private readonly ConcurrentDictionary<string, Tick> _latestTicks = new();
    private readonly Subject<Tick> _allTicks = new();
    private IDisposable? _feedSubscription;
    private long _tickCount;
    private long _tickCountAtLastSample;
    private DateTime _lastSampleTime = DateTime.UtcNow;

    /// <summary>Latest tick per token.</summary>
    public IReadOnlyDictionary<string, Tick> LatestTicks => _latestTicks;

    /// <summary>Total ticks received since start.</summary>
    public long TotalTickCount => _tickCount;

    /// <summary>Ticks per second (rolling average).</summary>
    public double TicksPerSecond
    {
        get
        {
            var elapsed = (DateTime.UtcNow - _lastSampleTime).TotalSeconds;
            if (elapsed < 0.1) return 0;
            var tps = (_tickCount - _tickCountAtLastSample) / elapsed;
            _tickCountAtLastSample = _tickCount;
            _lastSampleTime = DateTime.UtcNow;
            return tps;
        }
    }

    /// <summary>Timestamp of the last received tick.</summary>
    public DateTime LastTickTime { get; private set; }

    public TickDispatcher(IMarketDataService marketData, ILogger<TickDispatcher> logger)
    {
        _marketData = marketData;
        _logger = logger;
    }

    /// <summary>Start listening to the market data tick stream.</summary>
    public void Start()
    {
        _feedSubscription?.Dispose();
        _feedSubscription = _marketData.TickStream.Subscribe(
            tick =>
            {
                Interlocked.Increment(ref _tickCount);
                LastTickTime = DateTime.Now;
                _latestTicks[tick.Token] = tick;
                _allTicks.OnNext(tick);
            },
            ex => _logger.LogError(ex, "TickDispatcher: feed error")
        );
        _logger.LogInformation("TickDispatcher started");
    }

    /// <summary>Get a UI-safe observable for a specific token, throttled to ~4fps.</summary>
    public IObservable<Tick> GetTickStream(string token)
    {
        return _allTicks
            .Where(t => t.Token == token)
            .Sample(TimeSpan.FromMilliseconds(250));
    }

    /// <summary>Get a UI-safe observable for all ticks, batched at ~4fps.</summary>
    public IObservable<IList<Tick>> GetBatchedStream()
    {
        return _allTicks
            .Buffer(TimeSpan.FromMilliseconds(250))
            .Where(batch => batch.Count > 0);
    }

    /// <summary>Get the latest tick for a token, or null.</summary>
    public Tick? GetLatest(string token)
        => _latestTicks.TryGetValue(token, out var tick) ? tick : null;

    public void Dispose()
    {
        _feedSubscription?.Dispose();
        _allTicks.Dispose();
    }
}
