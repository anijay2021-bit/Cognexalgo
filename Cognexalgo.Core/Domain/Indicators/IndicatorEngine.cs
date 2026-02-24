using System;
using System.Collections.Generic;
using System.Linq;
using Cognexalgo.Core.Domain.Enums;
using Skender.Stock.Indicators;

namespace Cognexalgo.Core.Domain.Indicators
{
    /// <summary>
    /// Unified Indicator Engine (Module 2B):
    /// Wraps Skender.Stock.Indicators with warm-up detection,
    /// multi-timeframe support, and a clean API for signal evaluation.
    /// 
    /// Supports all 14 indicators:
    /// Trend: EMA, SMA, SuperTrend, VWAP, Bollinger Bands
    /// Momentum: RSI, MACD, Stochastic, CCI
    /// Volatility: ATR
    /// Volume: Volume SMA, OBV
    /// Special: LTP, StaticValue
    /// </summary>
    public class IndicatorEngine
    {
        private readonly Dictionary<TimeFrame, List<Quote>> _candlesByTimeframe = new();
        private readonly Dictionary<string, double> _indicatorCache = new();

        /// <summary>
        /// Returns true when all indicators have enough historical data.
        /// </summary>
        public bool IsWarmedUp { get; private set; } = false;

        public int MinimumBarsRequired { get; set; } = 200;

        // ─── Data Management ─────────────────────────────────────

        /// <summary>Add a closed candle for a specific timeframe.</summary>
        public void AddCandle(TimeFrame tf, Quote candle)
        {
            if (!_candlesByTimeframe.ContainsKey(tf))
                _candlesByTimeframe[tf] = new List<Quote>();

            _candlesByTimeframe[tf].Add(candle);

            // Limit history to 500 candles per timeframe
            if (_candlesByTimeframe[tf].Count > 500)
                _candlesByTimeframe[tf].RemoveAt(0);

            // Check warm-up
            CheckWarmUp();
        }

        /// <summary>Load bulk historical data for a timeframe.</summary>
        public void LoadHistory(TimeFrame tf, List<Quote> candles)
        {
            _candlesByTimeframe[tf] = candles.TakeLast(500).ToList();
            CheckWarmUp();
        }

        public int GetCandleCount(TimeFrame tf)
        {
            return _candlesByTimeframe.ContainsKey(tf) ? _candlesByTimeframe[tf].Count : 0;
        }

        private void CheckWarmUp()
        {
            // Check if the primary timeframe has enough data
            IsWarmedUp = _candlesByTimeframe.Any(kv => kv.Value.Count >= MinimumBarsRequired);
        }

        // ─── Indicator Calculations ─────────────────────────────

        /// <summary>
        /// Get the value of an indicator for a specific timeframe.
        /// </summary>
        /// <param name="type">Indicator type</param>
        /// <param name="period">Indicator period (e.g., 14 for RSI(14))</param>
        /// <param name="tf">Timeframe to calculate on</param>
        /// <param name="offset">0 = current bar, 1 = previous bar, etc.</param>
        /// <returns>Indicator value, or double.NaN if not available.</returns>
        public double GetValue(IndicatorType type, int period, TimeFrame tf, int offset = 0)
        {
            if (!_candlesByTimeframe.ContainsKey(tf) || _candlesByTimeframe[tf].Count < 2)
                return double.NaN;

            var candles = _candlesByTimeframe[tf];
            int idx = candles.Count - 1 - offset;
            if (idx < 0) return double.NaN;

            try
            {
                return type switch
                {
                    IndicatorType.EMA => GetEma(candles, period, idx),
                    IndicatorType.SMA => GetSma(candles, period, idx),
                    IndicatorType.RSI => GetRsi(candles, period, idx),
                    IndicatorType.MACD => GetMacd(candles, idx),
                    IndicatorType.SuperTrend => GetSuperTrend(candles, period, idx),
                    IndicatorType.BollingerBands => GetBollingerBands(candles, period, idx),
                    IndicatorType.ATR => GetAtr(candles, period, idx),
                    IndicatorType.Stochastic => GetStochastic(candles, period, idx),
                    IndicatorType.CCI => GetCci(candles, period, idx),
                    IndicatorType.VWAP => GetVwap(candles, idx),
                    IndicatorType.VolumeSMA => GetVolumeSma(candles, period, idx),
                    IndicatorType.OBV => GetObv(candles, idx),
                    IndicatorType.LTP => (double)candles[idx].Close,
                    IndicatorType.StaticValue => 0, // Handled by condition evaluator
                    _ => double.NaN
                };
            }
            catch
            {
                return double.NaN;
            }
        }

        // ─── Individual Indicator Implementations ────────────────

        private double GetEma(List<Quote> candles, int period, int idx)
        {
            var results = candles.GetEma(period).ToList();
            return idx < results.Count && results[idx].Ema.HasValue
                ? (double)results[idx].Ema.Value : double.NaN;
        }

        private double GetSma(List<Quote> candles, int period, int idx)
        {
            var results = candles.GetSma(period).ToList();
            return idx < results.Count && results[idx].Sma.HasValue
                ? (double)results[idx].Sma.Value : double.NaN;
        }

        private double GetRsi(List<Quote> candles, int period, int idx)
        {
            var results = candles.GetRsi(period).ToList();
            return idx < results.Count && results[idx].Rsi.HasValue
                ? (double)results[idx].Rsi.Value : double.NaN;
        }

        private double GetMacd(List<Quote> candles, int idx)
        {
            // MACD default: fast=12, slow=26, signal=9
            var results = candles.GetMacd(12, 26, 9).ToList();
            return idx < results.Count && results[idx].Macd.HasValue
                ? (double)results[idx].Macd.Value : double.NaN;
        }

        private double GetSuperTrend(List<Quote> candles, int period, int idx)
        {
            // SuperTrend with ATR multiplier (default period=10, multiplier=3)
            var results = candles.GetSuperTrend(period, 3).ToList();
            return idx < results.Count && results[idx].SuperTrend.HasValue
                ? (double)results[idx].SuperTrend.Value : double.NaN;
        }

        private double GetBollingerBands(List<Quote> candles, int period, int idx)
        {
            // Returns the middle band (SMA). Upper/Lower accessed separately if needed.
            var results = candles.GetBollingerBands(period, 2).ToList();
            return idx < results.Count && results[idx].Sma.HasValue
                ? (double)results[idx].Sma.Value : double.NaN;
        }

        private double GetAtr(List<Quote> candles, int period, int idx)
        {
            var results = candles.GetAtr(period).ToList();
            return idx < results.Count && results[idx].Atr.HasValue
                ? (double)results[idx].Atr.Value : double.NaN;
        }

        private double GetStochastic(List<Quote> candles, int period, int idx)
        {
            // Returns %K value
            var results = candles.GetStoch(period, 3, 3).ToList();
            return idx < results.Count && results[idx].K.HasValue
                ? (double)results[idx].K.Value : double.NaN;
        }

        private double GetCci(List<Quote> candles, int period, int idx)
        {
            var results = candles.GetCci(period).ToList();
            return idx < results.Count && results[idx].Cci.HasValue
                ? (double)results[idx].Cci.Value : double.NaN;
        }

        private double GetVwap(List<Quote> candles, int idx)
        {
            // VWAP with session reset — uses full day's candles
            var results = candles.GetVwap().ToList();
            return idx < results.Count && results[idx].Vwap.HasValue
                ? (double)results[idx].Vwap.Value : double.NaN;
        }

        private double GetVolumeSma(List<Quote> candles, int period, int idx)
        {
            // Simple average of volume over N periods
            if (idx < period - 1) return double.NaN;
            double sum = 0;
            for (int i = idx - period + 1; i <= idx; i++)
                sum += (double)candles[i].Volume;
            return sum / period;
        }

        private double GetObv(List<Quote> candles, int idx)
        {
            var results = candles.GetObv().ToList();
            return idx < results.Count ? results[idx].Obv : double.NaN;
        }

        // ─── Snapshot for Signal Logging ─────────────────────────

        /// <summary>
        /// Captures current values of all active indicators for the signal audit log.
        /// </summary>
        public Dictionary<string, double> GetSnapshot(TimeFrame tf, List<(IndicatorType Type, int Period)> indicators)
        {
            var snapshot = new Dictionary<string, double>();
            foreach (var (type, period) in indicators)
            {
                string key = period > 0 ? $"{type}({period})" : type.ToString();
                snapshot[key] = GetValue(type, period, tf, 0);
            }
            return snapshot;
        }
    }
}
