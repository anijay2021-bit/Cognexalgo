using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cognexalgo.Core;
using Cognexalgo.Core.Models;
using Cognexalgo.Core.Strategies;
using Xunit;

namespace Cognexalgo.Tests
{
    public class MissedSignalTest
    {
        [Fact]
        public async Task Test_DynamicStrategy_GeneratesSignal_MidCandle()
        {
            // 1. Setup minimal TradingEngine (mocking DBs not required for pure logic test if isolated, 
            // but we'll use a mocked/paper engine).
            var engine = new TradingEngine();
            engine.IsPaperTrading = true;
            
            // 2. Setup Dynamic Strategy with an EMA Crossover/Touch rule
            string configJson = @"{
                ""StrategyName"": ""EMA_Touch_Test"",
                ""Symbol"": ""NIFTY"",
                ""Timeframe"": ""1m"",
                ""TotalLots"": 1,
                ""EntryRules"": [
                    {
                        ""Action"": ""BUY"",
                        ""Conditions"": [
                            {
                                ""Indicator"": ""EMA"",
                                ""Period"": 9,
                                ""Operator"": ""GREATER_THAN"",
                                ""SourceType"": ""StaticValue"",
                                ""StaticValue"": 10000 
                            },
                            {
                                ""Indicator"": ""LTP"",
                                ""Period"": 1,
                                ""Operator"": ""GREATER_THAN"",
                                ""SourceType"": ""StaticValue"",
                                ""StaticValue"": 25050 
                            }
                        ]
                    }
                ],
                ""ExitRules"": [],
                ""ExitSettings"": {}
            }";

            var strategy = new DynamicStrategy(engine, configJson);
            strategy.IsActive = true;

            int signalCount = 0;
            strategy.OnSignalGenerated += (signal) => 
            {
                signalCount++;
                Console.WriteLine($"Signal Generated: {signal.SignalType} @ {signal.Price}");
            };

            // 3. Pump normal ticks below the threshold (25000)
            DateTime startTime = DateTime.Now.Date.AddHours(9).AddMinutes(15);
            
            // Inject mock history so EMA(9) can be calculated!
            var mockHistory = new List<Skender.Stock.Indicators.Quote>();
            for (int i = 0; i < 15; i++)
            {
                mockHistory.Add(new Skender.Stock.Indicators.Quote
                {
                    Date = startTime.AddMinutes(-15 + i),
                    Open = 25000,
                    High = 25000,
                    Low = 25000,
                    Close = 25000,
                    Volume = 100
                });
            }
            await strategy.InitializeAsync(mockHistory);
            
            // Simulated first tick of the minute
            await strategy.OnTickAsync(new TickerData 
            { 
                Nifty = new InstrumentInfo { Ltp = 25000 } 
            });

            Assert.Equal(0, signalCount);

            // 4. Pump a rapid spike tick mid-minute that exceeds threshold (25051)
            await strategy.OnTickAsync(new TickerData 
            { 
                Nifty = new InstrumentInfo { Ltp = 25060 } 
            });

            // If tick-level evaluation works, it should trigger now!
            Assert.Equal(1, signalCount);

            // 5. Pump a retracement tick still within the same minute
            await strategy.OnTickAsync(new TickerData 
            { 
                Nifty = new InstrumentInfo { Ltp = 25010 } 
            });

            // State should prevent infinite triggers, or at least we caught the first one.
            // Depending on duplicate prevention logic, we just care > 0.
            Assert.True(signalCount >= 1);
        }
    }
}
