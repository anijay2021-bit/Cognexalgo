using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Skender.Stock.Indicators;

namespace Cognexalgo.Core.Services
{
    /// <summary>
    /// Builds real-time OHLCV candles from live tick data for a single symbol+interval.
    ///
    /// Usage:
    ///   1. Call Seed() after historical data is loaded from LiteDB.
    ///   2. Call AddTick() on every live price update.
    ///   3. Call GetFullSeries() to get history + current open candle for indicator calculations.
    ///   4. OnCandleClosed event fires each time a bar completes — also persisted to LiteDB.
    /// </summary>
    public class CandleAggregator
    {
        private readonly int _timeframeMinutes;
        private readonly string _symbol;
        private readonly string _interval;
        private readonly HistoryCacheService _cache; // null → no persistence

        private Quote _currentCandle;
        private readonly List<Quote> _history = new();

        public Quote CurrentCandle => _currentCandle;

        /// <summary>Fires when a candle bar closes (the completed bar is the argument).</summary>
        public event Action<Quote> OnCandleClosed;

        // ── Constructors ───────────────────────────────────────────────────────

        /// <summary>
        /// Full constructor — used by TradingEngine for engine-level aggregators
        /// that are seeded from LiteDB and persist completed candles back to it.
        /// </summary>
        public CandleAggregator(string symbol, string interval, int timeframeMinutes,
                                HistoryCacheService cache = null)
        {
            _symbol           = symbol;
            _interval         = interval;
            _timeframeMinutes = timeframeMinutes;
            _cache            = cache;
        }

        /// <summary>
        /// Convenience constructor for strategy-level aggregators that only need
        /// in-memory aggregation (no symbol metadata, no persistence).
        /// Keeps existing callers in DynamicStrategy / HybridStraddleStrategy compiling unchanged.
        /// </summary>
        public CandleAggregator(int timeframeMinutes)
            : this(symbol: null, interval: null, timeframeMinutes, cache: null) { }

        // ── Seeding ───────────────────────────────────────────────────────────

        /// <summary>
        /// Pre-load historical quotes downloaded from Angel One / LiteDB.
        /// Must be called before AddTick() to give indicators a warm series.
        /// </summary>
        public void Seed(List<Quote> history)
        {
            _history.Clear();
            if (history != null) _history.AddRange(history);
        }

        // ── Live tick ingestion ───────────────────────────────────────────────

        public void AddTick(DateTime timestamp, decimal price)
        {
            var candleTime = GetCandleStartTime(timestamp, _timeframeMinutes);

            if (_currentCandle == null)
            {
                StartNewCandle(candleTime, price);
            }
            else if (candleTime > _currentCandle.Date)
            {
                // Previous candle just closed
                CloseAndPersist(_currentCandle);
                StartNewCandle(candleTime, price);
            }
            else
            {
                _currentCandle.High  = Math.Max(_currentCandle.High, price);
                _currentCandle.Low   = Math.Min(_currentCandle.Low,  price);
                _currentCandle.Close = price;
            }
        }

        // ── Query ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the complete series: all closed historical bars + the current open candle.
        /// Use this as input to Skender indicators so they reflect both history and live price.
        /// </summary>
        public List<Quote> GetFullSeries()
        {
            var result = new List<Quote>(_history);
            if (_currentCandle != null)
                result.Add(_currentCandle);
            return result;
        }

        // ── Internals ─────────────────────────────────────────────────────────

        private void CloseAndPersist(Quote closed)
        {
            OnCandleClosed?.Invoke(closed);
            _history.Add(closed);

            // Fire-and-forget write to LiteDB (non-blocking)
            if (_cache != null && _symbol != null && _interval != null)
                Task.Run(() => _cache.UpsertCandle(_symbol, _interval, closed));
        }

        private void StartNewCandle(DateTime startTime, decimal price)
        {
            _currentCandle = new Quote
            {
                Date  = startTime,
                Open  = price,
                High  = price,
                Low   = price,
                Close = price
            };
        }

        private static DateTime GetCandleStartTime(DateTime dt, int interval)
            => new DateTime(dt.Year, dt.Month, dt.Day,
                            dt.Hour, (dt.Minute / interval) * interval, 0);
    }
}
