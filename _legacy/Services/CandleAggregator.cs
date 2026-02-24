using System;
using System.Collections.Generic;
using System.Linq;
using Skender.Stock.Indicators;

namespace Cognexalgo.Core.Services
{
    public class CandleAggregator
    {
        private readonly int _timeframeMinutes;
        private Quote _currentCandle;
        public Quote CurrentCandle => _currentCandle;

        public event Action<Quote> OnCandleClosed;

        public CandleAggregator(int timeframeMinutes)
        {
            _timeframeMinutes = timeframeMinutes;
        }

        public void AddTick(DateTime timestamp, decimal price)
        {
            // Align to timeframe boundary (e.g. 1 min, 5 min)
            var candleTime = GetCandleStartTime(timestamp, _timeframeMinutes);

            if (_currentCandle == null)
            {
                StartNewCandle(candleTime, price);
            }
            else if (candleTime > _currentCandle.Date)
            {
                // Previous candle closed
                OnCandleClosed?.Invoke(_currentCandle);
                StartNewCandle(candleTime, price);
            }
            else
            {
                // Update current candle
                _currentCandle.High = Math.Max(_currentCandle.High, price);
                _currentCandle.Low = Math.Min(_currentCandle.Low, price);
                _currentCandle.Close = price;
            }
        }

        private void StartNewCandle(DateTime startTime, decimal price)
        {
            _currentCandle = new Quote
            {
                Date = startTime,
                Open = price,
                High = price,
                Low = price,
                Close = price
            };
        }

        private DateTime GetCandleStartTime(DateTime dt, int interval)
        {
            return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, (dt.Minute / interval) * interval, 0);
        }
    }
}
