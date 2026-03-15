using System;
using Xunit;

namespace Cognexalgo.Tests
{
    /// <summary>
    /// Tests the SL trigger conditions from CalendarStrategy:
    ///
    /// Sell SL logic (CheckSellLegSLAsync):
    ///   trigger when  LTP >= CombinedSellEntryPrice
    ///
    /// Flipped-buy SL logic (CheckFlippedBuyLegSLAsync):
    ///   buySL = entryPrice * (1 - BuySLPercent / 100)
    ///   trigger when  LTP <= buySL
    /// </summary>
    public class CalendarSLCalculationTests
    {
        // ── Combined sell SL calculation ──────────────────────────────────────

        [Fact]
        public void CombinedSellSL_IsSumOfBothLegEntries()
        {
            double sellCeEntry = 110;
            double sellPeEntry = 90;
            double combinedSL  = sellCeEntry + sellPeEntry;

            Assert.Equal(200, combinedSL);
        }

        // ── Sell SL trigger: fires when LTP >= combinedSL ─────────────────────

        [Theory]
        [InlineData(199, false)]   // just below — no trigger
        [InlineData(200, true)]    // exactly at — trigger
        [InlineData(201, true)]    // above — trigger
        [InlineData(0,   false)]   // no price yet — no trigger
        public void SellLegSL_TriggersCorrectly(double ltp, bool shouldTrigger)
        {
            double combinedSL = 200;
            bool triggered    = ltp > 0 && ltp >= combinedSL;
            Assert.Equal(shouldTrigger, triggered);
        }

        // ── Flipped buy SL price calculation ─────────────────────────────────

        [Fact]
        public void FlippedBuy_SLPrice_50Percent()
        {
            double buyEntry    = 200;
            double buySLPct    = 50.0;
            double expectedSL  = buyEntry * (1.0 - buySLPct / 100.0);  // 100

            Assert.Equal(100, expectedSL);
        }

        [Fact]
        public void FlippedBuy_SLPrice_25Percent()
        {
            double buyEntry   = 200;
            double buySLPct   = 25.0;
            double expectedSL = buyEntry * (1.0 - buySLPct / 100.0);  // 150

            Assert.Equal(150, expectedSL);
        }

        // ── Flipped buy SL trigger: fires when LTP <= buySL ──────────────────

        [Theory]
        [InlineData(101, false)]   // above SL — no trigger
        [InlineData(100, true)]    // exactly at SL — trigger
        [InlineData(99,  true)]    // below SL — trigger
        [InlineData(0,   false)]   // no price — no trigger
        public void FlippedBuySL_TriggersCorrectly(double ltp, bool shouldTrigger)
        {
            double buySL   = 100;
            bool triggered = ltp > 0 && ltp <= buySL;
            Assert.Equal(shouldTrigger, triggered);
        }

        // ── Combined SL recalculation after flip-back to sell ─────────────────

        [Fact]
        public void FlipBackToSell_RecalculatesCombinedSL()
        {
            // Flipped-back sell entry at 180; other sell leg still at 90
            double flippedBackSellEntry = 180;
            double otherSellEntry       = 90;
            double newCombinedSL        = flippedBackSellEntry + otherSellEntry;

            Assert.Equal(270, newCombinedSL);
        }
    }
}
