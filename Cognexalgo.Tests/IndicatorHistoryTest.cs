using System;
using System.Collections.Generic;
using System.Linq;
using Cognexalgo.Core.Rules;
using Skender.Stock.Indicators;
using Xunit;

namespace Cognexalgo.Tests
{
    public class IndicatorHistoryTest
    {
        [Fact]
        public void Test_EMA200_WithShortHistory_ReturnsNonZero()
        {
            // 1. Setup history with only 20 candles
            var history = new List<Quote>();
            for (int i = 0; i < 20; i++)
            {
                history.Add(new Quote
                {
                    Date = DateTime.Now.AddMinutes(-20 + i),
                    Open = 25000 + i,
                    High = 25010 + i,
                    Low = 24990 + i,
                    Close = 25005 + i
                });
            }

            var context = new EvaluationContext(history);

            // 2. Request EMA 200
            // Previously this would return 0 due to count < period check
            double emaValue = context.GetIndicatorValue(IndicatorType.EMA, 200);

            // 3. Verify it's not 0 or NaN
            // Skender's Ema will calculate on available data if possible or return null (which we map to 0 in GetIndicatorValue if null)
            // But we want to ensure our FIX (removing the early return 0) allows it to at least try.
            Assert.True(emaValue > 0, $"EMA 200 should return a value > 0 even with 20 candles, but got {emaValue}");
            Console.WriteLine($"EMA 200 with 20 candles returned: {emaValue}");
        }
    }
}
