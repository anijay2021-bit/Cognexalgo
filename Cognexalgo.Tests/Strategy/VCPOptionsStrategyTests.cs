using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Cognexalgo.Core.Application.Interfaces;
using Cognexalgo.Core.Domain.Patterns;
using Cognexalgo.Core.Models;
using Cognexalgo.Core.Services.Strategy;
using Xunit;

namespace Cognexalgo.Tests.Strategy
{
    // ── Test fixtures ─────────────────────────────────────────────────────────

    public sealed class VCPOptionsStrategyTests
    {
        // Fresh mock instances per test (xUnit creates a new instance per test method)
        private readonly Mock<IVCPDetector>            _detector    = new();
        private readonly Mock<IVCPRiskManager>         _riskMgr     = new();
        private readonly Mock<IVCPSettingsService>     _settings    = new();
        private readonly Mock<IMarketDataService>      _marketData  = new();
        private readonly Mock<IOrderManagementService> _oms         = new();
        private readonly Mock<IBrokerFactory>          _factory     = new();

        // ── Factory helpers ───────────────────────────────────────────────────

        private VCPOptionsStrategy CreateStrategy(VCPSettings? cfg = null)
        {
            _settings.Setup(s => s.Load()).Returns(cfg ?? new VCPSettings());
            _factory.Setup(f => f.GetActiveMode(It.IsAny<VCPTradingMode>()))
                    .Returns(VCPTradingMode.PaperTrade);

            return new VCPOptionsStrategy(
                _detector.Object,
                _riskMgr.Object,
                _settings.Object,
                _marketData.Object,
                _oms.Object,
                _factory.Object,
                NullLogger<VCPOptionsStrategy>.Instance);
        }

        /// <summary>
        /// Returns a valid 2-contraction VCPPattern for NIFTY with the specified key prices.
        /// C1: High=120, Low=70 → SwingPercent≈41.7%, AvgVol=6000
        /// C2: High=pivotLevel, Low=tightLow → SwingPercent smaller, AvgVol=2000 (&lt;6000×0.65)
        /// </summary>
        private static VCPPattern MakePattern(decimal pivotLevel = 100m, decimal tightLow = 90m) =>
            new()
            {
                Symbol      = "NIFTY",
                PivotLevel  = pivotLevel,
                TightLow    = tightLow,
                Quality     = VCPQuality.A,
                Timeframe   = "Daily",
                DetectedAt  = DateTime.Now,
                Contractions = new List<Contraction>
                {
                    new() { SwingHigh = 120m, SwingLow = 70m,       AvgVolume = 6000,
                            StartDate = DateTime.Today.AddDays(-12), EndDate = DateTime.Today.AddDays(-6) },
                    new() { SwingHigh = pivotLevel, SwingLow = tightLow, AvgVolume = 2000,
                            StartDate = DateTime.Today.AddDays(-6),  EndDate = DateTime.Today },
                },
            };

        private static Candle MakeCandle(decimal close, string symbol = "NIFTY",
                                         string timeframe = "Daily") =>
            new()
            {
                Symbol    = symbol,
                Open      = close - 1m,
                High      = close + 2m,
                Low       = close - 2m,
                Close     = close,
                Volume    = 10_000,
                Timestamp = DateTime.Now,
                Timeframe = timeframe,
            };

        private static List<Candle> FakeCandleHistory(int count = 65, decimal baseClose = 100m) =>
            Enumerable.Range(0, count)
                      .Select(i => MakeCandle(baseClose + i * 0.1m))
                      .ToList();

        // ── GivenOpenSignal ───────────────────────────────────────────────────

        /// <summary>
        /// Seeds the strategy with one open trade by simulating a breakout candle.
        /// After this helper returns:
        ///   entry = <paramref name="entry"/>, SL = <paramref name="sl"/>
        ///   T1    = entry + (entry − sl) × 1.5   (default Target1RR)
        ///   T2    = entry + (entry − sl) × 3.0
        /// Mocks are reset so that subsequent candles do NOT create new signals.
        /// </summary>
        private async Task<(VCPOptionsStrategy strategy, VCPSignal signal)> GivenOpenSignalAsync(
            VCPSettings? cfg = null, decimal entry = 100m, decimal sl = 90m)
        {
            var pattern  = MakePattern(pivotLevel: entry, tightLow: sl);
            var strategy = CreateStrategy(cfg);

            VCPSignal? captured = null;
            strategy.OnSignalGenerated += (_, s) => captured = s;

            _riskMgr.Setup(r => r.CanOpenTrade(It.IsAny<int>(), It.IsAny<int>()))
                    .Returns(true);
            _marketData.Setup(m => m.GetCandlesAsync(
                    It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(FakeCandleHistory());
            _detector.Setup(d => d.Detect(It.IsAny<List<Candle>>(), "NIFTY", It.IsAny<string>()))
                     .Returns(pattern);
            _detector.Setup(d => d.IsBreakingOut(pattern, It.IsAny<Candle>()))
                     .Returns(true);
            _oms.Setup(o => o.PlaceOrderAsync(It.IsAny<VCPOrder>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("ORDER001");

            await strategy.OnNewCandle(MakeCandle(entry));

            captured.Should().NotBeNull("breakout candle should have generated a signal");

            // Reset detector so subsequent candles don't create duplicate signals
            _detector.Setup(d => d.Detect(It.IsAny<List<Candle>>(), It.IsAny<string>(), It.IsAny<string>()))
                     .Returns((VCPPattern?)null);
            _detector.Setup(d => d.IsBreakingOut(It.IsAny<VCPPattern>(), It.IsAny<Candle>()))
                     .Returns(false);
            _detector.Setup(d => d.IsPatternFailed(It.IsAny<VCPPattern>(), It.IsAny<Candle>()))
                     .Returns(false);
            _detector.Setup(d => d.IsReversalCandle(It.IsAny<Candle>(), It.IsAny<Candle>()))
                     .Returns(false);

            // Reset OMS so Verify counts start from 0 for subsequent assertions
            _oms.Invocations.Clear();

            return (strategy, captured!);
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task Test_OnNewCandle_ExitsTrade_WhenStopLossHit()
        {
            // entry=100, sl=90 — fire a candle at 85 (below SL)
            var (strategy, _) = await GivenOpenSignalAsync(entry: 100m, sl: 90m);

            VCPTradeResult? result = null;
            strategy.OnTradeCompleted += (_, r) => result = r;

            await strategy.OnNewCandle(MakeCandle(85m));   // Close=85 < SL=90

            result.Should().NotBeNull("SL breach should have closed the trade");
            result!.ExitTrigger.Should().Be(ExitTrigger.StopLossHit);
            _oms.Verify(
                o => o.PlaceOrderAsync(
                    It.Is<VCPOrder>(ord => ord.TransactionType == "SELL"),
                    It.IsAny<CancellationToken>()),
                Times.Once,
                "exactly one SELL order should have been placed");
        }

        [Fact]
        public async Task Test_OnNewCandle_PartialExit_WhenTarget1Hit()
        {
            // entry=100, sl=90  →  T1 = 100 + (100-90)*1.5 = 115
            var (strategy, _) = await GivenOpenSignalAsync(entry: 100m, sl: 90m);

            await strategy.OnNewCandle(MakeCandle(116m));  // Close=116 >= T1=115

            _oms.Verify(
                o => o.PlaceOrderAsync(
                    It.Is<VCPOrder>(ord => ord.TransactionType == "SELL"),
                    It.IsAny<CancellationToken>()),
                Times.Once,
                "T1 partial exit should place exactly one SELL order");
        }

        [Fact]
        public async Task Test_OnNewCandle_MovesSlToBreakeven_AfterTarget1()
        {
            // Use 4 lots so 50% partial exit leaves 2 lots still open
            var cfg = new VCPSettings { FixedLotsPerTrade = 4 };
            var (strategy, _) = await GivenOpenSignalAsync(cfg: cfg, entry: 100m, sl: 90m);

            // T1 hit — exits 2 lots, SL moves to entry (100)
            await strategy.OnNewCandle(MakeCandle(116m));

            // Fire a candle between original SL (90) and entry (100):
            //   original SL=90 → would NOT trigger; moved SL=100 → SHOULD trigger
            VCPTradeResult? result = null;
            strategy.OnTradeCompleted += (_, r) => result = r;

            await strategy.OnNewCandle(MakeCandle(95m));   // 90 < 95 < 100

            result.Should().NotBeNull(
                "after T1, SL should have moved to entry=100; Close=95 should trigger it");
            result!.ExitTrigger.Should().Be(ExitTrigger.StopLossHit);
        }

        [Fact]
        public async Task Test_OnNewCandle_SkipsNewSignal_WhenMaxTradesReached()
        {
            // CanOpenTrade always returns false → no signal should ever be created
            _riskMgr.Setup(r => r.CanOpenTrade(It.IsAny<int>(), It.IsAny<int>()))
                    .Returns(false);

            _marketData.Setup(m => m.GetCandlesAsync(
                    It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(FakeCandleHistory());

            var strategy = CreateStrategy();

            await strategy.OnNewCandle(MakeCandle(100m));

            _oms.Verify(
                o => o.PlaceOrderAsync(It.IsAny<VCPOrder>(), It.IsAny<CancellationToken>()),
                Times.Never,
                "no order should be placed when CanOpenTrade is false");
        }

        [Fact]
        public async Task Test_OnNewCandle_DoesNotThrow_WhenExceptionOccurs()
        {
            // GetCandlesAsync throws — the strategy must swallow it
            _riskMgr.Setup(r => r.CanOpenTrade(It.IsAny<int>(), It.IsAny<int>()))
                    .Returns(true);
            _marketData.Setup(m => m.GetCandlesAsync(
                    It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("feed unavailable"));

            var strategy = CreateStrategy();

            var act = () => strategy.OnNewCandle(MakeCandle(100m));
            await act.Should().NotThrowAsync(
                "exceptions inside OnNewCandle must never propagate to the caller");
        }
    }
}
