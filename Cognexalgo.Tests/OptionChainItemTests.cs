using System;
using Cognexalgo.Core.Models;
using Xunit;

namespace Cognexalgo.Tests
{
    /// <summary>
    /// Tests computed properties on OptionChainItem:
    ///   IsCall, IsPut, DaysToExpiry (min 1), IsWeeklyExpiry heuristic.
    /// </summary>
    public class OptionChainItemTests
    {
        // ── IsCall / IsPut ────────────────────────────────────────────────────

        [Fact]
        public void IsCall_TrueWhenOptionTypeIsCE()
        {
            var item = new OptionChainItem { OptionType = "CE" };
            Assert.True(item.IsCall);
            Assert.False(item.IsPut);
        }

        [Fact]
        public void IsPut_TrueWhenOptionTypeIsPE()
        {
            var item = new OptionChainItem { OptionType = "PE" };
            Assert.True(item.IsPut);
            Assert.False(item.IsCall);
        }

        [Fact]
        public void IsCall_FalseForPE()
        {
            var item = new OptionChainItem { OptionType = "PE" };
            Assert.False(item.IsCall);
        }

        [Fact]
        public void IsPut_FalseForCE()
        {
            var item = new OptionChainItem { OptionType = "CE" };
            Assert.False(item.IsPut);
        }

        // ── DaysToExpiry ──────────────────────────────────────────────────────

        [Fact]
        public void DaysToExpiry_MinimumIsOne_ForPastDate()
        {
            // Expiry in the past — DaysToExpiry must not go below 1 (protects
            // Black-Scholes from division-by-zero)
            var item = new OptionChainItem
            {
                OptionType = "CE",
                ExpiryDate = DateTime.Today.AddDays(-5)
            };
            Assert.Equal(1, item.DaysToExpiry);
        }

        [Fact]
        public void DaysToExpiry_MinimumIsOne_ForToday()
        {
            var item = new OptionChainItem
            {
                OptionType = "CE",
                ExpiryDate = DateTime.Today
            };
            Assert.Equal(1, item.DaysToExpiry);
        }

        [Fact]
        public void DaysToExpiry_CorrectForFutureDate()
        {
            var item = new OptionChainItem
            {
                OptionType = "CE",
                ExpiryDate = DateTime.Today.AddDays(7)
            };
            Assert.Equal(7, item.DaysToExpiry);
        }

        [Fact]
        public void DaysToExpiry_NeverZeroOrNegative()
        {
            foreach (int daysAgo in new[] { 0, 1, 5, 30, 365 })
            {
                var item = new OptionChainItem
                {
                    OptionType = "CE",
                    ExpiryDate = DateTime.Today.AddDays(-daysAgo)
                };
                Assert.True(item.DaysToExpiry >= 1,
                    $"DaysToExpiry must be >= 1, got {item.DaysToExpiry} for -{daysAgo} days");
            }
        }

        // ── IsWeeklyExpiry ────────────────────────────────────────────────────
        //
        // Heuristic: ExpiryDate.AddDays(7).Month == ExpiryDate.Month
        // → True  = weekly (next Thursday still in same month)
        // → False = monthly (next Thursday rolls into next month = last Thursday)
        //
        // 2026 Thursday dates:
        //   Jan: 1,8,15,22,29  → 29 is last (29+7=Feb5 → different month)
        //   Mar: 5,12,19,26    → 26 is last (26+7=Apr2 → different month)
        //   Mar 19 + 7 = Mar 26 → same month → weekly

        [Fact]
        public void IsWeeklyExpiry_TrueWhenNextThursdayInSameMonth()
        {
            // March 19 2026 (Thursday): March 19 + 7 = March 26 (same month) → weekly
            var item = new OptionChainItem
            {
                OptionType = "CE",
                ExpiryDate = new DateTime(2026, 3, 19)
            };
            Assert.True(item.IsWeeklyExpiry);
        }

        [Fact]
        public void IsWeeklyExpiry_FalseWhenNextThursdayInNextMonth()
        {
            // March 26 2026 (Thursday): March 26 + 7 = April 2 (different month) → monthly
            var item = new OptionChainItem
            {
                OptionType = "CE",
                ExpiryDate = new DateTime(2026, 3, 26)
            };
            Assert.False(item.IsWeeklyExpiry);
        }

        [Fact]
        public void IsWeeklyExpiry_TrueForEarlyMonthThursday()
        {
            // January 8 2026 (Thursday): Jan 8 + 7 = Jan 15 (same month) → weekly
            var item = new OptionChainItem
            {
                OptionType = "PE",
                ExpiryDate = new DateTime(2026, 1, 8)
            };
            Assert.True(item.IsWeeklyExpiry);
        }

        [Fact]
        public void IsWeeklyExpiry_FalseForLastThursdayOfMonth()
        {
            // January 29 2026 (Thursday): Jan 29 + 7 = Feb 5 (different month) → monthly
            var item = new OptionChainItem
            {
                OptionType = "PE",
                ExpiryDate = new DateTime(2026, 1, 29)
            };
            Assert.False(item.IsWeeklyExpiry);
        }

        [Fact]
        public void IsWeeklyExpiry_DefaultExpiryDate_FallsBackToDteHeuristic()
        {
            // ExpiryDate == default → fallback: DaysToExpiry <= 10
            var nearItem = new OptionChainItem { OptionType = "CE" };  // ExpiryDate = default
            // DaysToExpiry = Math.Max(1, (default.Date - Today).Days) → very negative → clamped to 1
            Assert.True(nearItem.IsWeeklyExpiry);  // 1 <= 10 → true
        }

        // ── TradingSymbol alias ───────────────────────────────────────────────

        [Fact]
        public void TradingSymbol_ReturnsSymbolValue()
        {
            var item = new OptionChainItem
            {
                OptionType = "CE",
                Symbol     = "NIFTY27MAR2623450CE"
            };
            Assert.Equal("NIFTY27MAR2623450CE", item.TradingSymbol);
        }

        [Fact]
        public void TradingSymbol_ReturnsEmptyStringWhenSymbolIsNull()
        {
            var item = new OptionChainItem { OptionType = "CE", Symbol = null };
            Assert.Equal("", item.TradingSymbol);
        }
    }
}
