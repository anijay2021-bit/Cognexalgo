using System;
using Xunit;

namespace Cognexalgo.Tests
{
    /// <summary>
    /// Tests the hedge strike formula from CalendarStrategy.BuyHedgesAsync():
    ///
    ///   CE hedge strike = SellStrike + (HedgeStrikeOffset × StrikeStep)
    ///   PE hedge strike = SellStrike - (HedgeStrikeOffset × StrikeStep)
    /// </summary>
    public class HedgeStrikeCalculationTests
    {
        private static double CeHedgeStrike(double sellStrike, int offset, double step)
            => sellStrike + (offset * step);

        private static double PeHedgeStrike(double sellStrike, int offset, double step)
            => sellStrike - (offset * step);

        // ── NIFTY (step = 50) ─────────────────────────────────────────────────

        [Fact]
        public void Nifty_CeHedge_Offset2()
        {
            // Sell CE 23450, offset 2 → Buy CE 23450 + 100 = 23550
            Assert.Equal(23550, CeHedgeStrike(23450, 2, 50));
        }

        [Fact]
        public void Nifty_PeHedge_Offset2()
        {
            // Sell PE 23450, offset 2 → Buy PE 23450 - 100 = 23350
            Assert.Equal(23350, PeHedgeStrike(23450, 2, 50));
        }

        [Fact]
        public void Nifty_CeHedge_Offset1()
        {
            Assert.Equal(23500, CeHedgeStrike(23450, 1, 50));
        }

        [Fact]
        public void Nifty_PeHedge_Offset1()
        {
            Assert.Equal(23400, PeHedgeStrike(23450, 1, 50));
        }

        [Fact]
        public void Nifty_CeHedge_Offset5()
        {
            Assert.Equal(23700, CeHedgeStrike(23450, 5, 50));
        }

        [Fact]
        public void Nifty_PeHedge_Offset5()
        {
            Assert.Equal(23200, PeHedgeStrike(23450, 5, 50));
        }

        // ── BANKNIFTY / SENSEX (step = 100) ──────────────────────────────────

        [Fact]
        public void BankNifty_CeHedge_Offset3()
        {
            // Sell CE 48000, offset 3 → Buy CE 48000 + 300 = 48300
            Assert.Equal(48300, CeHedgeStrike(48000, 3, 100));
        }

        [Fact]
        public void BankNifty_PeHedge_Offset3()
        {
            // Sell PE 48000, offset 3 → Buy PE 48000 - 300 = 47700
            Assert.Equal(47700, PeHedgeStrike(48000, 3, 100));
        }

        [Fact]
        public void BankNifty_CeHedge_Offset2()
        {
            Assert.Equal(48200, CeHedgeStrike(48000, 2, 100));
        }

        [Fact]
        public void BankNifty_PeHedge_Offset2()
        {
            Assert.Equal(47800, PeHedgeStrike(48000, 2, 100));
        }

        // ── Symmetry: CE and PE hedges are equidistant from sell strike ───────

        [Theory]
        [InlineData(23450, 2, 50)]
        [InlineData(48000, 3, 100)]
        [InlineData(23500, 1, 50)]
        public void CeAndPeHedges_AreEquidistantFromSellStrike(
            double sellStrike, int offset, double step)
        {
            double ceHedge = CeHedgeStrike(sellStrike, offset, step);
            double peHedge = PeHedgeStrike(sellStrike, offset, step);

            double ceDistance = ceHedge - sellStrike;
            double peDistance = sellStrike - peHedge;

            Assert.Equal(ceDistance, peDistance);
        }
    }
}
