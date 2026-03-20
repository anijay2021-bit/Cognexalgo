using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Cognexalgo.Core.Application.Interfaces;
using Cognexalgo.Core.Domain.Patterns;
using Cognexalgo.Core.Models;

namespace Cognexalgo.Core.Services.Detection
{
    /// <summary>
    /// Detects Volatility Contraction Patterns (VCP) in a candle series.
    /// Stateless — all context is passed as parameters; safe to register as singleton.
    /// </summary>
    public sealed class VCPDetector : IVCPDetector
    {
        private readonly ILogger<VCPDetector> _logger;

        public VCPDetector(ILogger<VCPDetector> logger)
        {
            _logger = logger;
        }

        /// <inheritdoc/>
        public VCPPattern? Detect(List<Candle> candles, string symbol, string timeframe)
        {
            try
            {
                // Guard: minimum viable candle count
                if (candles == null || candles.Count < 10)
                    return null;

                // ── STEP 1: UPTREND CHECK ─────────────────────────────────────
                int smaWindow = Math.Min(50, candles.Count);
                decimal sma50 = candles.TakeLast(smaWindow).Average(c => c.Close);
                if (candles[^1].Close <= sma50)
                    return null;

                // ── STEP 2: SWING DETECTION on last 60 candles ───────────────
                var segment = candles.TakeLast(60).ToList();

                // Need at least 5 candles to test 2 neighbours on each side
                if (segment.Count < 5)
                    return null;

                var swingHighs = new List<SwingPoint>();
                var swingLows  = new List<SwingPoint>();

                for (int i = 2; i <= segment.Count - 3; i++)
                {
                    decimal hi = segment[i].High;
                    if (hi > segment[i - 2].High &&
                        hi > segment[i - 1].High &&
                        hi > segment[i + 1].High &&
                        hi > segment[i + 2].High)
                    {
                        swingHighs.Add(new SwingPoint(i, hi, segment[i].Timestamp, IsHigh: true));
                    }

                    decimal lo = segment[i].Low;
                    if (lo < segment[i - 2].Low &&
                        lo < segment[i - 1].Low &&
                        lo < segment[i + 1].Low &&
                        lo < segment[i + 2].Low)
                    {
                        swingLows.Add(new SwingPoint(i, lo, segment[i].Timestamp, IsHigh: false));
                    }
                }

                // Build alternating H / L / H / L sequence, taking the best candidate
                // when consecutive swings of the same type appear before alternation.
                var swingPoints = BuildAlternatingSwings(swingHighs, swingLows);

                // Minimum 4 swings → 2 contractions
                if (swingPoints.Count < 4)
                    return null;

                // ── STEP 3: BUILD CONTRACTIONS ────────────────────────────────
                var contractions = new List<Contraction>();

                for (int i = 0; i + 1 < swingPoints.Count; i += 2)
                {
                    SwingPoint high = swingPoints[i];
                    SwingPoint low  = swingPoints[i + 1];

                    // Sequence must be H → L; any mismatch means the alternation broke
                    if (!high.IsHigh || low.IsHigh)
                        break;

                    long avgVol = ComputeAvgVolume(segment, high.Index, low.Index);

                    contractions.Add(new Contraction
                    {
                        SwingHigh  = high.Price,
                        SwingLow   = low.Price,
                        AvgVolume  = avgVol,
                        StartDate  = high.Date,
                        EndDate    = low.Date,
                    });
                }

                // ── STEP 4: VALIDATE SHRINKING ────────────────────────────────
                if (contractions.Count < 2 || contractions.Count > 5)
                    return null;

                for (int i = 1; i < contractions.Count; i++)
                {
                    if (contractions[i].SwingPercent >= contractions[i - 1].SwingPercent)
                        return null;
                }

                // ── STEP 5: VOLUME DRY-UP ─────────────────────────────────────
                bool allVolumeZero = contractions.All(c => c.AvgVolume == 0);
                if (allVolumeZero)
                {
                    _logger.LogWarning(
                        "[VCPDetector] Volume data is zero for all contractions on {Symbol}. " +
                        "Skipping volume dry-up check.", symbol);
                }
                else
                {
                    long firstVol = contractions[0].AvgVolume;
                    long lastVol  = contractions[^1].AvgVolume;

                    // firstVol guard: if first contraction somehow has zero volume, skip
                    if (firstVol > 0 && lastVol >= firstVol * 0.65m)
                        return null;
                }

                // ── STEP 6: GRADE THE PATTERN ─────────────────────────────────
                decimal tightPercent = contractions[^1].SwingPercent;
                VCPQuality quality = tightPercent < 1.0m  ? VCPQuality.A
                                   : tightPercent < 2.5m  ? VCPQuality.B
                                   :                        VCPQuality.C;

                // ── STEP 7: BUILD AND RETURN ──────────────────────────────────
                return new VCPPattern
                {
                    Symbol       = symbol,
                    Timeframe    = timeframe,
                    Contractions = contractions,
                    PivotLevel   = contractions[^1].SwingHigh,
                    TightLow     = contractions[^1].SwingLow,
                    DetectedAt   = DateTime.Now,
                    Quality      = quality,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[VCPDetector] Unhandled exception in Detect() for symbol={Symbol}", symbol);
                return null;
            }
        }

        /// <inheritdoc/>
        public bool IsBreakingOut(VCPPattern pattern, Candle currentCandle)
        {
            if (pattern?.Contractions == null || pattern.Contractions.Count == 0)
                return false;

            long lastContAvgVol = pattern.Contractions[^1].AvgVolume;

            bool priceBreakout  = currentCandle.Close > pattern.PivotLevel;

            // If the contraction has no volume data, skip the volume gate
            bool volumeBreakout = lastContAvgVol == 0
                                  || currentCandle.Volume > (long)(lastContAvgVol * 1.5m);

            return priceBreakout && volumeBreakout;
        }

        /// <inheritdoc/>
        public bool IsPatternFailed(VCPPattern pattern, Candle currentCandle)
        {
            if (pattern == null) return false;
            return currentCandle.Close < pattern.TightLow;
        }

        /// <inheritdoc/>
        public bool IsReversalCandle(Candle current, Candle previous)
        {
            if (current == null || previous == null) return false;

            decimal currentBody  = Math.Abs(current.Close  - current.Open);
            decimal previousBody = Math.Abs(previous.Close - previous.Open);

            // ── Bearish engulfing ─────────────────────────────────────────────
            // Current opens at or above previous close, then closes below previous open,
            // with a body at least 20% larger than the prior candle's body.
            bool bearishEngulfing = current.Open  >= previous.Close
                                 && current.Close  <  previous.Open
                                 && currentBody    >  previousBody * 1.2m;

            if (bearishEngulfing) return true;

            // ── Shooting star ─────────────────────────────────────────────────
            // Long upper wick (> 2× body), body in the lower 30% of the candle's range,
            // candle must close bearish.
            decimal upperWick   = current.High - Math.Max(current.Open, current.Close);
            decimal candleRange = current.High - current.Low;
            decimal bodyTop     = Math.Max(current.Open, current.Close);

            bool bodyInLower30 = candleRange > 0
                              && bodyTop <= current.Low + candleRange * 0.3m;

            bool shootingStar  = current.Close < current.Open
                              && upperWick     > 2m * currentBody
                              && bodyInLower30;

            return shootingStar;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Merges raw swing-high and swing-low lists into a strictly alternating H/L sequence.
        /// When two consecutive swings of the same type appear, the better one is kept
        /// (highest high, lowest low) before the sequence alternates.
        /// The sequence always starts with a swing high.
        /// </summary>
        private static List<SwingPoint> BuildAlternatingSwings(
            List<SwingPoint> highs, List<SwingPoint> lows)
        {
            // Merge and sort by candle index
            var all = highs.Concat(lows)
                           .OrderBy(s => s.Index)
                           .ToList();

            if (all.Count == 0) return new();

            // Find the first swing high to anchor the alternating sequence
            int startIdx = all.FindIndex(s => s.IsHigh);
            if (startIdx < 0) return new();

            var result = new List<SwingPoint>();

            for (int i = startIdx; i < all.Count; i++)
            {
                SwingPoint s = all[i];

                if (result.Count == 0)
                {
                    result.Add(s);
                    continue;
                }

                bool lastIsHigh = result[^1].IsHigh;

                if (s.IsHigh == lastIsHigh)
                {
                    // Same type as the current tail — keep the better candidate
                    bool replaceBetter = s.IsHigh
                        ? s.Price > result[^1].Price    // higher swing high
                        : s.Price < result[^1].Price;   // lower swing low

                    if (replaceBetter)
                        result[^1] = s;
                }
                else
                {
                    // Opposite type — extend the alternating chain
                    result.Add(s);
                }
            }

            return result;
        }

        /// <summary>
        /// Computes the average candle volume between two segment indices (inclusive).
        /// Handles out-of-range indices safely by clamping.
        /// </summary>
        private static long ComputeAvgVolume(List<Candle> segment, int fromIdx, int toIdx)
        {
            // Normalise direction
            if (fromIdx > toIdx) (fromIdx, toIdx) = (toIdx, fromIdx);
            fromIdx = Math.Max(0, fromIdx);
            toIdx   = Math.Min(segment.Count - 1, toIdx);

            long total = 0L;
            int  count = 0;

            for (int i = fromIdx; i <= toIdx; i++)
            {
                total += segment[i].Volume;
                count++;
            }

            return count == 0 ? 0L : total / count;
        }

        // ── Inner type ────────────────────────────────────────────────────────

        /// <summary>Lightweight value holder for a single identified swing point.</summary>
        private sealed record SwingPoint(int Index, decimal Price, DateTime Date, bool IsHigh);
    }
}
