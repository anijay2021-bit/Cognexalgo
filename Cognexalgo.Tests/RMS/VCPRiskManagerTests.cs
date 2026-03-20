using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Cognexalgo.Core.Services.Risk;
using Xunit;

namespace Cognexalgo.Tests.RMS
{
    public sealed class VCPRiskManagerTests
    {
        private static VCPRiskManager Create() =>
            new VCPRiskManager(NullLogger<VCPRiskManager>.Instance);

        // ── CalculateLots ──────────────────────────────────────────────────────

        [Fact]
        public void Test_CalculateLots_ReturnsMinimumOne_WhenRiskTooSmall()
        {
            // riskAmount=10, slPerUnit=50, lotSize=75 → slPerLot=3750 → lots=0 → clamp to 1
            var rm = Create();
            int lots = rm.CalculateLots(entryPrice: 200m, stopLoss: 150m,
                                        riskAmountPerTrade: 10m, lotSize: 75);
            lots.Should().Be(1);
        }

        [Fact]
        public void Test_CalculateLots_CapsAtTen_WhenRiskVeryLarge()
        {
            // riskAmount=10_000_000, slPerUnit=1, lotSize=1 → raw lots >> 10 → clamp to 10
            var rm = Create();
            int lots = rm.CalculateLots(entryPrice: 200m, stopLoss: 199m,
                                        riskAmountPerTrade: 10_000_000m, lotSize: 1);
            lots.Should().Be(10);
        }

        [Fact]
        public void Test_CalculateLots_CorrectFormula_NormalCase()
        {
            // riskAmount=2000, slPerUnit=(entryPrice-stopLoss)=50, lotSize=75
            // slPerLot = 50 * 75 = 3750
            // lots = Floor(2000 / 3750) = 0 → clamp to 1
            var rm = Create();
            int lots = rm.CalculateLots(entryPrice: 200m, stopLoss: 150m,
                                        riskAmountPerTrade: 2000m, lotSize: 75);
            lots.Should().Be(1);
        }

        [Fact]
        public void Test_CalculateLots_ReturnsOne_WhenSlDistanceIsZero()
        {
            var rm = Create();
            int lots = rm.CalculateLots(entryPrice: 200m, stopLoss: 200m,
                                        riskAmountPerTrade: 5000m, lotSize: 75);
            lots.Should().Be(1);
        }

        [Fact]
        public void Test_CalculateLots_ReturnsOne_WhenEntryPriceBelowStopLoss()
        {
            // Negative SL distance → slDistancePerLot <= 0 → safe default 1
            var rm = Create();
            int lots = rm.CalculateLots(entryPrice: 100m, stopLoss: 200m,
                                        riskAmountPerTrade: 5000m, lotSize: 75);
            lots.Should().Be(1);
        }

        [Fact]
        public void Test_CalculateLots_ReturnsOne_WhenAnyInputIsZero()
        {
            var rm = Create();
            rm.CalculateLots(0m, 0m, 0m, 0).Should().Be(1);
        }

        // ── CanOpenTrade ───────────────────────────────────────────────────────

        [Fact]
        public void Test_CanOpenTrade_ReturnsFalse_WhenAtMaxCapacity()
        {
            var rm = Create();
            rm.CanOpenTrade(currentOpenTrades: 2, maxAllowedTrades: 2).Should().BeFalse();
        }

        [Fact]
        public void Test_CanOpenTrade_ReturnsTrue_WhenBelowCapacity()
        {
            var rm = Create();
            rm.CanOpenTrade(currentOpenTrades: 1, maxAllowedTrades: 2).Should().BeTrue();
        }

        [Fact]
        public void Test_CanOpenTrade_ClampsMaxTo4()
        {
            // maxAllowedTrades=10 → clamped to 4; currentOpenTrades=4 → false
            var rm = Create();
            rm.CanOpenTrade(currentOpenTrades: 4, maxAllowedTrades: 10).Should().BeFalse();
        }

        [Fact]
        public void Test_CanOpenTrade_ClampsMaxTo4_AllowsBelow()
        {
            // maxAllowedTrades=10 → clamped to 4; currentOpenTrades=3 → true
            var rm = Create();
            rm.CanOpenTrade(currentOpenTrades: 3, maxAllowedTrades: 10).Should().BeTrue();
        }

        // ── CalculatePositionSize ──────────────────────────────────────────────

        [Fact]
        public void Test_CalculatePositionSize_ReturnsCorrectValue()
        {
            var rm = Create();
            // 2 lots × 75 units × 150.00 premium = 22,500.00
            rm.CalculatePositionSize(lots: 2, lotSize: 75, premium: 150m)
              .Should().Be(22_500m);
        }

        [Fact]
        public void Test_CalculatePositionSize_RoundsToTwoDecimals()
        {
            var rm = Create();
            // 1 × 1 × 1.006 → rounds to 1.01 unambiguously (not a banker's-rounding midpoint)
            rm.CalculatePositionSize(lots: 1, lotSize: 1, premium: 1.006m)
              .Should().Be(1.01m);
        }
    }
}
