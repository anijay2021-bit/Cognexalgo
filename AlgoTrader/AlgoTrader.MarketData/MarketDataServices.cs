using System.Reactive.Linq;
using System.Reactive.Subjects;
using AlgoTrader.Core.Enums;
using AlgoTrader.Core.Interfaces;
using AlgoTrader.Core.Models;
using AlgoTrader.Data.EfCore;
using Microsoft.Extensions.Logging;

namespace AlgoTrader.MarketData;

/// <summary>Fetches historical candles from broker with local DB caching.</summary>
public class HistoricalDataManager
{
    private readonly IBrokerFactory _brokerFactory;
    private readonly CandleRepository _candleRepo;
    private readonly ILogger<HistoricalDataManager> _logger;

    public HistoricalDataManager(IBrokerFactory brokerFactory, CandleRepository candleRepo, ILogger<HistoricalDataManager> logger)
    {
        _brokerFactory = brokerFactory;
        _candleRepo = candleRepo;
        _logger = logger;
    }

    /// <summary>Get candles with DB cache — only fetches gaps from API.</summary>
    public async Task<List<Candle>> GetCandlesWithCacheAsync(
        string symbol, string token, Exchange exchange, TimeFrame interval,
        DateTime from, DateTime to, AccountCredential account)
    {
        // Check DB first
        var cached = await _candleRepo.GetCandlesAsync(symbol, interval, from, to);
        if (cached.Count > 0)
        {
            _logger.LogDebug("Returning {Count} cached candles for {Symbol}", cached.Count, symbol);
            return cached;
        }

        // Fetch from broker
        var broker = _brokerFactory.Create(account.BrokerType);
        var candles = await broker.GetHistoricalDataAsync(account.JWTToken, token, exchange, interval, from, to);

        foreach (var c in candles)
            c.Symbol = symbol;

        // Store in DB
        if (candles.Count > 0)
            await _candleRepo.UpsertCandlesAsync(candles);

        _logger.LogInformation("Fetched and cached {Count} candles for {Symbol} from API", candles.Count, symbol);
        return candles;
    }

    /// <summary>Get the last N candles from DB.</summary>
    public async Task<List<Candle>> GetRecentCandlesAsync(string symbol, TimeFrame interval, int count)
    {
        var from = DateTime.UtcNow.AddDays(-30);
        var to = DateTime.UtcNow;
        var candles = await _candleRepo.GetCandlesAsync(symbol, interval, from, to);
        return candles.TakeLast(count).ToList();
    }
}

/// <summary>Builds real-time OHLC candles from tick stream.</summary>
public class LiveCandleBuilder : IDisposable
{
    private readonly Subject<Candle> _candleSubject = new();
    private readonly Dictionary<string, Candle> _buildingCandles = new();
    private readonly TimeFrame _interval;
    private IDisposable? _subscription;
    private readonly ILogger<LiveCandleBuilder> _logger;

    public IObservable<Candle> CandleStream => _candleSubject.AsObservable();

    public LiveCandleBuilder(TimeFrame interval, ILogger<LiveCandleBuilder> logger)
    {
        _interval = interval;
        _logger = logger;
    }

    public void Start(IObservable<Tick> tickStream)
    {
        _subscription = tickStream.Subscribe(OnTick);
        _logger.LogInformation("LiveCandleBuilder started for {Interval}", _interval);
    }

    private void OnTick(Tick tick)
    {
        var candleStart = GetCandleStartTime(tick.Timestamp, _interval);
        var key = $"{tick.Token}_{candleStart:yyyyMMddHHmm}";

        if (!_buildingCandles.TryGetValue(key, out var candle))
        {
            // Check if a previous candle for this token needs to be emitted
            var prevKey = _buildingCandles.Keys.FirstOrDefault(k => k.StartsWith(tick.Token + "_") && k != key);
            if (prevKey != null)
            {
                _candleSubject.OnNext(_buildingCandles[prevKey]);
                _buildingCandles.Remove(prevKey);
            }

            candle = new Candle
            {
                Token = tick.Token,
                Symbol = tick.Symbol,
                Interval = _interval,
                Timestamp = candleStart,
                Open = tick.LTP,
                High = tick.LTP,
                Low = tick.LTP,
                Close = tick.LTP,
                Volume = tick.Volume
            };
            _buildingCandles[key] = candle;
        }
        else
        {
            candle.High = Math.Max(candle.High, tick.LTP);
            candle.Low = Math.Min(candle.Low, tick.LTP);
            candle.Close = tick.LTP;
            candle.Volume = tick.Volume;
        }
    }

    private static DateTime GetCandleStartTime(DateTime timestamp, TimeFrame interval)
    {
        int minutes = interval switch
        {
            TimeFrame.ONE_MINUTE => 1,
            TimeFrame.THREE_MINUTE => 3,
            TimeFrame.FIVE_MINUTE => 5,
            TimeFrame.FIFTEEN_MINUTE => 15,
            TimeFrame.THIRTY_MINUTE => 30,
            TimeFrame.ONE_HOUR => 60,
            TimeFrame.ONE_DAY => 1440,
            _ => 1
        };

        var totalMinutes = (int)(timestamp.TimeOfDay.TotalMinutes);
        var candleMinute = (totalMinutes / minutes) * minutes;
        return timestamp.Date.AddMinutes(candleMinute);
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _candleSubject.Dispose();
    }
}

/// <summary>Technical indicator calculations using Skender.Stock.Indicators.</summary>
public class IndicatorEngine
{
    public Task<IEnumerable<Skender.Stock.Indicators.EmaResult>> CalcEMAAsync(List<Candle> candles, int period)
    {
        var quotes = CandlesToQuotes(candles);
        var result = Skender.Stock.Indicators.Indicator.GetEma(quotes, period);
        return Task.FromResult(result);
    }

    public Task<IEnumerable<Skender.Stock.Indicators.RsiResult>> CalcRSIAsync(List<Candle> candles, int period)
    {
        var quotes = CandlesToQuotes(candles);
        var result = Skender.Stock.Indicators.Indicator.GetRsi(quotes, period);
        return Task.FromResult(result);
    }

    public Task<IEnumerable<Skender.Stock.Indicators.MacdResult>> CalcMACDAsync(List<Candle> candles, int fast = 12, int slow = 26, int signal = 9)
    {
        var quotes = CandlesToQuotes(candles);
        var result = Skender.Stock.Indicators.Indicator.GetMacd(quotes, fast, slow, signal);
        return Task.FromResult(result);
    }

    public Task<IEnumerable<Skender.Stock.Indicators.BollingerBandsResult>> CalcBollingerAsync(List<Candle> candles, int period = 20, double stdDev = 2.0)
    {
        var quotes = CandlesToQuotes(candles);
        var result = Skender.Stock.Indicators.Indicator.GetBollingerBands(quotes, period, stdDev);
        return Task.FromResult(result);
    }

    public Task<IEnumerable<Skender.Stock.Indicators.AtrResult>> CalcATRAsync(List<Candle> candles, int period = 14)
    {
        var quotes = CandlesToQuotes(candles);
        var result = Skender.Stock.Indicators.Indicator.GetAtr(quotes, period);
        return Task.FromResult(result);
    }

    private static IEnumerable<Skender.Stock.Indicators.Quote> CandlesToQuotes(List<Candle> candles)
    {
        return candles.Select(c => new Skender.Stock.Indicators.Quote
        {
            Date = c.Timestamp,
            Open = c.Open,
            High = c.High,
            Low = c.Low,
            Close = c.Close,
            Volume = c.Volume
        });
    }
}
