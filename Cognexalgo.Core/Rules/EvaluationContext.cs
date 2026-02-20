using System;
using System.Collections.Generic;
using System.Linq;
using Skender.Stock.Indicators;

namespace Cognexalgo.Core.Rules
{
    public class EvaluationContext
    {
        public List<Quote> Candidates { get; private set; }
        private Dictionary<string, double> _overrides = new Dictionary<string, double>();
        
        // Helper to get LTP (Close of last candle)
        public double LTP => Candidates != null && Candidates.Any() ? (double)Candidates.Last().Close : 0;

        public EvaluationContext(List<Quote> history)
        {
             Candidates = history ?? new List<Quote>();
        }

        public void AddCandidate(Quote quote)
        {
            Candidates.Add(quote);
        }

        public void SetIndicator(IndicatorType type, int period, double value)
        {
            string key = $"{type}_{period}";
            if (_overrides.ContainsKey(key))
                _overrides[key] = value;
            else
                _overrides.Add(key, value);
        }

        /// <summary>
        /// Calculates and retrieves the indicator value.
        /// </summary>
        public double GetIndicatorValue(IndicatorType type, int period, int offset = 0)
        {
            string key = $"{type}_{period}";
            // Only use override for offset 0 (Current Value)
            if (offset == 0 && _overrides.ContainsKey(key)) return _overrides[key];

            if (Candidates.Count < period) return 0;

            try 
            {
                // We need to calculate the indicator on the whole history
                // Optimization: In a real high-freq engine, we would cache these series.
                // For now, calculating on valid history is fine for 1-sec timeframe.
                
                switch (type)
                {
                    case IndicatorType.SMA:
                        var sma = Candidates.GetSma(period).ToList();
                        if (sma.Count <= offset) return 0;
                        return (double)(sma[sma.Count - 1 - offset].Sma ?? 0);

                    case IndicatorType.EMA:
                        var ema = Candidates.GetEma(period).ToList();
                        if (ema.Count <= offset) return 0;
                        return (double)(ema[ema.Count - 1 - offset].Ema ?? 0);

                    case IndicatorType.RSI:
                        var rsi = Candidates.GetRsi(period).ToList();
                        if (rsi.Count <= offset) return 0;
                        return (double)(rsi[rsi.Count - 1 - offset].Rsi ?? 0);
                        
                    case IndicatorType.MACD:
                        var macd = Candidates.GetMacd(12, 26, 9).ToList(); 
                        return GetMacdValue(macd, offset, "MACD"); 

                    case IndicatorType.SUPERTREND:
                        var st = Candidates.GetSuperTrend(period, 3).ToList();
                        return GetSuperTrendValue(st, offset);

                    case IndicatorType.ATR:
                        var atr = Candidates.GetAtr(period).ToList();
                        if (atr.Count <= offset) return 0;
                        return (double)(atr[atr.Count - 1 - offset].Atr ?? 0);
                    
                    case IndicatorType.BOLLINGER_BANDS:
                        var bb = Candidates.GetBollingerBands(period, 2).ToList();
                        if (bb.Count <= offset) return 0;
                        return (double)(bb[bb.Count - 1 - offset].UpperBand ?? 0);

                     case IndicatorType.LTP:
                        if (Candidates.Count <= offset) return 0;
                        return (double)Candidates[Candidates.Count - 1 - offset].Close;

                    default:
                        return 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Indicator Error] {type} Calculation Failed: {ex.Message}");
                return 0;
            }
        }

        // Removed GetValue as it was causing dynamic dispatch errors with Skender results

        // Special helpers for complex results
        private double GetMacdValue(List<MacdResult> results, int offset, string component)
        {
             if (results.Count <= offset) return 0;
             var item = results[results.Count - 1 - offset];
             if (component == "SIGNAL") return (double)(item.Signal ?? 0);
             if (component == "HIST") return (double)(item.Histogram ?? 0);
             return (double)(item.Macd ?? 0);
        }

        private double GetSuperTrendValue(List<SuperTrendResult> results, int offset)
        {
             if (results.Count <= offset) return 0;
             return (double)(results[results.Count - 1 - offset].SuperTrend ?? 0);
        }
    }
}
