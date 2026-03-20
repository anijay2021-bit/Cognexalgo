using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Cognexalgo.Core.Domain.Patterns;
using Cognexalgo.Core.Models;
using Cognexalgo.Core.Services.Detection;
using Xunit;

namespace Cognexalgo.Tests.Detection
{
    // ── CandleBuilder ─────────────────────────────────────────────────────────

    /// <summary>
    /// Fluent test-data builder for <see cref="Candle"/> objects.
    /// Provides sensible defaults so tests only specify what matters.
    /// </summary>
    public sealed class CandleBuilder
    {
        private string   _symbol    = "TEST";
        private string   _timeframe = "Daily";
        private decimal  _open      = 99.5m;
        private decimal  _high      = 105m;
        private decimal  _low       = 95m;
        private decimal  _close     = 100m;
        private long     _volume    = 1_000;
        private DateTime _timestamp = new DateTime(2024, 1, 1);

        public CandleBuilder WithSymbol(string symbol)      { _symbol    = symbol;    return this; }
        public CandleBuilder WithOpen(decimal open)         { _open      = open;      return this; }
        public CandleBuilder WithHigh(decimal high)         { _high      = high;      return this; }
        public CandleBuilder WithLow(decimal low)           { _low       = low;       return this; }
        public CandleBuilder WithClose(decimal close)       { _close     = close;     return this; }
        public CandleBuilder WithVolume(long volume)        { _volume    = volume;    return this; }
        public CandleBuilder WithTimestamp(DateTime ts)     { _timestamp = ts;        return this; }

        public Candle Build() => new Candle
        {
            Symbol    = _symbol,
            Timeframe = _timeframe,
            Open      = _open,
            High      = _high,
            Low       = _low,
            Close     = _close,
            Volume    = _volume,
            Timestamp = _timestamp,
        };

        // ── Static factory helpers ────────────────────────────────────────────

        /// <summary>
        /// Returns a default "flat trend" candle at an optional day offset.
        /// Open=99.5 High=105 Low=95 Close=100 Volume=1000.
        /// </summary>
        public static Candle Default(int dayOffset = 0) =>
            new CandleBuilder()
                .WithTimestamp(new DateTime(2024, 1, 1).AddDays(dayOffset))
                .Build();

        /// <summary>
        /// Returns a candle where Close is set to <paramref name="close"/>
        /// at a given day offset — all other values at defaults.
        /// </summary>
        public static Candle WithClose(decimal close, int dayOffset = 0) =>
            new CandleBuilder()
                .WithClose(close)
                .WithTimestamp(new DateTime(2024, 1, 1).AddDays(dayOffset))
                .Build();

        /// <summary>
        /// Builds a list of <paramref name="count"/> identical default candles,
        /// each one day apart.
        /// </summary>
        public static List<Candle> FlatList(int count, decimal close = 100m,
                                            long volume = 1_000)
        {
            var list = new List<Candle>(count);
            for (int i = 0; i < count; i++)
                list.Add(new CandleBuilder()
                    .WithClose(close)
                    .WithVolume(volume)
                    .WithTimestamp(new DateTime(2024, 1, 1).AddDays(i))
                    .Build());
            return list;
        }
    }

    // ── VCPDetectorTests ──────────────────────────────────────────────────────

    public sealed class VCPDetectorTests
    {
        // Inject NullLogger — zero overhead, no console noise in test output
        private readonly VCPDetector _sut =
            new VCPDetector(NullLogger<VCPDetector>.Instance);

        // ─────────────────────────────────────────────────────────────────────
        // Detect() — guard tests
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void Test_Detect_ReturnsNull_WhenCandlesLessThan10()
        {
            var candles = CandleBuilder.FlatList(count: 9);

            VCPPattern? result = _sut.Detect(candles, "TEST", "Daily");

            result.Should().BeNull();
        }

        [Fact]
        public void Test_Detect_ReturnsNull_WhenCandlesIsNull()
        {
            VCPPattern? result = _sut.Detect(null!, "TEST", "Daily");

            result.Should().BeNull();
        }

        [Fact]
        public void Test_Detect_ReturnsNull_WhenNotInUptrend()
        {
            // 65 candles — all close at 100, last candle close at 90
            // SMA50 ≈ 99.8 → 90 < 99.8 → uptrend check fails
            var candles = CandleBuilder.FlatList(count: 64);
            candles.Add(new CandleBuilder()
                .WithClose(90m)
                .WithTimestamp(new DateTime(2024, 1, 1).AddDays(64))
                .Build());

            VCPPattern? result = _sut.Detect(candles, "TEST", "Daily");

            result.Should().BeNull("price is below SMA50 so no uptrend");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Detect() — valid VCP
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void Test_Detect_ReturnsPattern_WhenValidVCPExists()
        {
            // Build a 65-candle series with an embedded 2-contraction VCP.
            //
            // Layout (segment = last 60 candles = orig[5..64]):
            //   seg[8]  (orig[13]) : swing high 1 → High=130
            //   seg[15] (orig[20]) : swing low  1 → Low=80
            //   seg[22] (orig[27]) : swing high 2 → High=120   (<130)
            //   seg[28] (orig[33]) : swing low  2 → Low=88     (>80)
            //
            // Contraction 1: (130-80)/130 = 38.46%
            // Contraction 2: (120-88)/120 = 26.67%  <  38.46% ✓
            //
            // Volume:
            //   C1 candles (seg[8]..seg[15]) volume=2000
            //   C2 candles (seg[22]..seg[28]) volume=1200
            //   1200 < 2000 × 0.65 = 1300  ✓
            //
            // Uptrend:  last 5 candles Close=110, rest=100
            //   SMA50 = (45×100 + 5×110) / 50 = 101
            //   currentClose=110 > 101 ✓

            var candles = BuildValidVcpCandles();

            VCPPattern? result = _sut.Detect(candles, "TEST", "Daily");

            result.Should().NotBeNull("a textbook 2-contraction VCP is present");
            result!.Symbol.Should().Be("TEST");
            result.ContractionCount.Should().Be(2);
            result.PivotLevel.Should().Be(120m,
                "PivotLevel = swing high of last contraction");
            result.TightLow.Should().Be(88m,
                "TightLow = swing low of last contraction");
            result.Quality.Should().Be(VCPQuality.C,
                "TightRangePercent ≈ 26.67% which is ≥ 2.5%");
            result.IsValid.Should().BeTrue();
        }

        // ─────────────────────────────────────────────────────────────────────
        // IsBreakingOut()
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void Test_IsBreakingOut_ReturnsFalse_WhenVolumeInsufficient()
        {
            // Pattern: PivotLevel=120, last contraction AvgVolume=1000
            // Candle: Close=121 (above pivot ✓), Volume=1400 (< 1000×1.5=1500 ✗)
            var pattern = BuildMinimalPattern(pivotLevel: 120m, lastContAvgVol: 1_000);
            Candle candle = new CandleBuilder()
                .WithClose(121m)
                .WithHigh(122m)
                .WithVolume(1_400)
                .Build();

            bool result = _sut.IsBreakingOut(pattern, candle);

            result.Should().BeFalse(
                "volume 1400 is below the required 1500 (1000 × 1.5)");
        }

        [Fact]
        public void Test_IsBreakingOut_ReturnsTrue_WhenBothConditionsMet()
        {
            var pattern = BuildMinimalPattern(pivotLevel: 120m, lastContAvgVol: 1_000);
            Candle candle = new CandleBuilder()
                .WithClose(121m)
                .WithHigh(122m)
                .WithVolume(1_600)   // > 1000 × 1.5 = 1500 ✓
                .Build();

            bool result = _sut.IsBreakingOut(pattern, candle);

            result.Should().BeTrue();
        }

        [Fact]
        public void Test_IsBreakingOut_ReturnsFalse_WhenPriceBelowPivot()
        {
            var pattern = BuildMinimalPattern(pivotLevel: 120m, lastContAvgVol: 500);
            Candle candle = new CandleBuilder()
                .WithClose(119m)     // below pivot ✗
                .WithVolume(2_000)
                .Build();

            _sut.IsBreakingOut(pattern, candle).Should().BeFalse();
        }

        // ─────────────────────────────────────────────────────────────────────
        // IsPatternFailed()
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void Test_IsPatternFailed_ReturnsTrue_WhenCloseBelowTightLow()
        {
            var pattern = BuildMinimalPattern(pivotLevel: 120m, tightLow: 100m);
            Candle candle = new CandleBuilder().WithClose(99m).Build();

            _sut.IsPatternFailed(pattern, candle).Should().BeTrue();
        }

        [Fact]
        public void Test_IsPatternFailed_ReturnsFalse_WhenCloseAboveTightLow()
        {
            var pattern = BuildMinimalPattern(pivotLevel: 120m, tightLow: 100m);
            Candle candle = new CandleBuilder().WithClose(101m).Build();

            _sut.IsPatternFailed(pattern, candle).Should().BeFalse();
        }

        // ─────────────────────────────────────────────────────────────────────
        // IsReversalCandle()
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void Test_IsReversalCandle_DetectsBearishEngulfing()
        {
            // Previous candle: bullish  Open=100  Close=105  body=5
            // Current candle:  bearish  Open=106 (≥ prev.Close=105)
            //                           Close=98  (< prev.Open=100)
            //                           body = |98-106| = 8  >  5×1.2=6  ✓
            Candle previous = new CandleBuilder()
                .WithOpen(100m).WithClose(105m)
                .WithHigh(107m).WithLow(99m)
                .Build();

            Candle current = new CandleBuilder()
                .WithOpen(106m).WithClose(98m)
                .WithHigh(108m).WithLow(97m)
                .Build();

            _sut.IsReversalCandle(current, previous).Should().BeTrue(
                "current engulfs previous with a body 60% larger");
        }

        [Fact]
        public void Test_IsReversalCandle_ReturnsFalse_WhenBodyTooSmall()
        {
            // Current body just barely does NOT exceed 120% of previous body
            // previous body = 5, current body = 5.9 (< 6 = 5×1.2)
            Candle previous = new CandleBuilder()
                .WithOpen(100m).WithClose(105m)
                .WithHigh(106m).WithLow(99m)
                .Build();

            Candle current = new CandleBuilder()
                .WithOpen(106m).WithClose(100.1m) // body = 5.9
                .WithHigh(107m).WithLow(99m)
                .Build();

            _sut.IsReversalCandle(current, previous).Should().BeFalse(
                "body is only 5.9, below the 6.0 threshold (5 × 1.2)");
        }

        [Fact]
        public void Test_IsReversalCandle_DetectsShootingStar()
        {
            // Candle: Open=100, Close=99 (bearish, body=1)
            //   High=108  → upper wick = 108 - 100 = 8  >  2×1 = 2  ✓
            //   Low=98    → range = 10
            //   bodyTop = max(100,99) = 100
            //   lower 30% threshold = 98 + 10×0.3 = 101  →  100 ≤ 101  ✓
            Candle previous = new CandleBuilder().WithClose(98m).Build();
            Candle current = new CandleBuilder()
                .WithOpen(100m).WithClose(99m)
                .WithHigh(108m).WithLow(98m)
                .Build();

            _sut.IsReversalCandle(current, previous).Should().BeTrue(
                "large upper wick with small bearish body in lower 30% of range");
        }

        [Fact]
        public void Test_IsReversalCandle_ReturnsFalse_WhenBullishCandle()
        {
            Candle previous = new CandleBuilder().WithClose(100m).Build();
            Candle current = new CandleBuilder()
                .WithOpen(99m).WithClose(105m)  // bullish — cannot be shooting star
                .WithHigh(110m).WithLow(98m)
                .Build();

            _sut.IsReversalCandle(current, previous).Should().BeFalse();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Private helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds 65 candles that embed a valid 2-contraction VCP in the last 60.
        /// See inline comments in Test_Detect_ReturnsPattern_WhenValidVCPExists.
        /// </summary>
        private static List<Candle> BuildValidVcpCandles()
        {
            const int total = 65;
            var candles = new List<Candle>(total);
            var baseDate = new DateTime(2024, 1, 1);

            for (int i = 0; i < total; i++)
            {
                // Last 5 candles trend up to satisfy SMA50 check
                decimal close  = i >= 60 ? 110m : 100m;
                decimal high   = 105m;   // default neighbour high
                decimal low    = 95m;    // default neighbour low
                long    volume = 1_000;

                // Segment indices (last 60) = original indices 5..64
                int seg = i - 5;  // valid only when i >= 5

                if (i >= 5)
                {
                    // ── Contraction 1 ─────────────────────────────────────────
                    // seg[8]  → swing high 1 (High=130, all 4 neighbours High=105)
                    if (seg == 8)  { high = 130m; volume = 2_000; }
                    // seg[9..14]: volume=2000 for contraction 1 avg volume
                    else if (seg >= 9 && seg <= 14) { volume = 2_000; }
                    // seg[15] → swing low 1 (Low=80, all 4 neighbours Low=95)
                    else if (seg == 15) { low = 80m; volume = 2_000; }

                    // ── Contraction 2 ─────────────────────────────────────────
                    // seg[22] → swing high 2 (High=120 < 130 ✓)
                    else if (seg == 22) { high = 120m; volume = 1_200; }
                    // seg[23..27]: volume=1200 for contraction 2 avg volume
                    else if (seg >= 23 && seg <= 27) { volume = 1_200; }
                    // seg[28] → swing low 2 (Low=88 > 80 ✓, Low=88 < default 95 ✓)
                    else if (seg == 28) { low = 88m; volume = 1_200; }
                }

                candles.Add(new CandleBuilder()
                    .WithClose(close)
                    .WithHigh(high)
                    .WithLow(low)
                    .WithOpen(close - 0.5m)
                    .WithVolume(volume)
                    .WithTimestamp(baseDate.AddDays(i))
                    .Build());
            }

            return candles;
        }

        /// <summary>
        /// Builds the minimal <see cref="VCPPattern"/> needed for breakout / failure / grading tests.
        /// </summary>
        private static VCPPattern BuildMinimalPattern(
            decimal pivotLevel,
            long    lastContAvgVol = 0,
            decimal tightLow       = 90m)
        {
            var lastContraction = new Contraction
            {
                SwingHigh  = pivotLevel,
                SwingLow   = tightLow,
                AvgVolume  = lastContAvgVol,
                StartDate  = DateTime.Today.AddDays(-10),
                EndDate    = DateTime.Today.AddDays(-5),
            };

            // A dummy first contraction (wider) so IsValid doesn't complain
            var firstContraction = new Contraction
            {
                SwingHigh  = pivotLevel + 20m,
                SwingLow   = tightLow   - 20m,
                AvgVolume  = lastContAvgVol * 2,
                StartDate  = DateTime.Today.AddDays(-20),
                EndDate    = DateTime.Today.AddDays(-12),
            };

            return new VCPPattern
            {
                Symbol       = "TEST",
                Timeframe    = "Daily",
                Contractions = new List<Contraction> { firstContraction, lastContraction },
                PivotLevel   = pivotLevel,
                TightLow     = tightLow,
                DetectedAt   = DateTime.Now,
                Quality      = VCPQuality.B,
            };
        }
    }
}
