using System;
using Xunit;

namespace Cognexalgo.Tests
{
    /// <summary>
    /// Tests the ATM strike rounding formula used by CalendarStrategy and PayoffBuilder:
    ///   Math.Round(spotPrice / strikeStep) * strikeStep
    /// </summary>
    public class AtmStrikeCalculationTests
    {
        // ── Helper mirrors CalendarStrategyConfig.StrikeStep ──────────────────
        private static double CalcAtm(double spot, double step) =>
            Math.Round(spot / step) * step;

        // ── NIFTY (step = 50) ─────────────────────────────────────────────────

        [Fact]
        public void Nifty_SpotBelowMidpoint_RoundsDown()
        {
            // 23323 / 50 = 466.46 → rounds to 466 → 23300
            Assert.Equal(23300, CalcAtm(23323, 50));
        }

        [Fact]
        public void Nifty_SpotExactlyAtMidpoint_RoundsToEven()
        {
            // 23275 / 50 = 465.5 → banker's rounding → 466 (even) → 23300
            Assert.Equal(23300, CalcAtm(23275, 50));
        }

        [Fact]
        public void Nifty_SpotAboveMidpoint_RoundsUp()
        {
            // 23276 / 50 = 465.52 → rounds to 466 → 23300
            Assert.Equal(23300, CalcAtm(23276, 50));
        }

        [Fact]
        public void Nifty_SpotExactlyOnStep_ReturnsItself()
        {
            Assert.Equal(23300, CalcAtm(23300, 50));
            Assert.Equal(23450, CalcAtm(23450, 50));
        }

        [Fact]
        public void Nifty_SpotJustBelowMidpoint_RoundsDown()
        {
            // 23274 / 50 = 465.48 → rounds to 465 → 23250
            Assert.Equal(23250, CalcAtm(23274, 50));
        }

        // ── BANKNIFTY / SENSEX (step = 100) ──────────────────────────────────

        [Fact]
        public void BankNifty_SpotBelowMidpoint_RoundsDown()
        {
            // 48123 / 100 = 481.23 → rounds to 481 → 48100
            Assert.Equal(48100, CalcAtm(48123, 100));
        }

        [Fact]
        public void BankNifty_SpotExactlyAtMidpoint_RoundsToEven()
        {
            // 48150 / 100 = 481.5 → banker's rounding → 482 (even) → 48200
            Assert.Equal(48200, CalcAtm(48150, 100));
        }

        [Fact]
        public void BankNifty_SpotAboveMidpoint_RoundsUp()
        {
            // 48151 / 100 = 481.51 → rounds to 482 → 48200
            Assert.Equal(48200, CalcAtm(48151, 100));
        }

        [Fact]
        public void BankNifty_SpotExactlyOnStep_ReturnsItself()
        {
            Assert.Equal(48000, CalcAtm(48000, 100));
            Assert.Equal(48100, CalcAtm(48100, 100));
        }

        [Fact]
        public void BankNifty_SpotJustBelowMidpoint_RoundsDown()
        {
            // 48149 / 100 = 481.49 → rounds to 481 → 48100
            Assert.Equal(48100, CalcAtm(48149, 100));
        }
    }
}
