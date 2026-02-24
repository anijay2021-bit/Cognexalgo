using System;
using System.Collections.Generic;
using Cognexalgo.Core;
using Cognexalgo.Core.Models;
using Cognexalgo.Core.Rules;
using Cognexalgo.Core.Strategies;
using Newtonsoft.Json;
using Skender.Stock.Indicators;
using System.Threading.Tasks;

namespace TempQuery
{
    class Program
    {
        static void Main(string[] args)
        {
            var engine = new TradingEngine();
            string configJson = @"{
                ""StrategyName"": ""EMA_Touch_Test"",
                ""Symbol"": ""NIFTY"",
                ""Timeframe"": ""1m"",
                ""TotalLots"": 1,
                ""EntryRules"": ""[{\""Action\"":\""BUY\"",\""Conditions\"":[{\""Indicator\"":\""EMA\"",\""Period\"":9,\""Operator\"":\""GREATER_THAN\"",\""SourceType\"":\""StaticValue\"",\""StaticValue\"":10000},{\""Indicator\"":\""LTP\"",\""Period\"":1,\""Operator\"":\""GREATER_THAN\"",\""SourceType\"":\""StaticValue\"",\""StaticValue\"":25050}]}]"",
                ""ExitRules"": ""[]"",
                ""ExitSettings"": {}
            }";

            var strategy = new DynamicStrategy(engine, configJson);
            strategy.IsActive = true;
            
            var config = JsonConvert.DeserializeObject<DynamicStrategyConfig>(configJson);
            Console.WriteLine($"Deserialized {config.EntryRules.Count} Entry Rules.");
            
            int signals = 0;
            strategy.OnSignalGenerated += (s) => 
            {
                signals++;
                Console.WriteLine($"Signal Fired: {s.SignalType}");
            };

            var history = new List<Skender.Stock.Indicators.Quote>();
            var startTime = DateTime.Now.AddHours(-1);
            for (int i = 0; i < 15; i++)
            {
                history.Add(new Skender.Stock.Indicators.Quote { Date = startTime.AddMinutes(i), Open = 25000, High = 25000, Low = 25000, Close = 25000, Volume = 100 });
            }

            strategy.InitializeAsync(history).Wait();
            
            Console.WriteLine("Simulating Tick > 25050");
            strategy.OnTickAsync(new TickerData { Nifty = new InstrumentInfo { Ltp = 25060 } }).Wait();
            
            Console.WriteLine("================ EVALUATOR CHECK ================");
            var ctx = new EvaluationContext(history);
            ctx.AddCandidate(new Quote { Date = DateTime.Now, Open=25060, High=25060, Low=25060, Close=25060, Volume=100 });
            var eval = new RuleEvaluator();
            foreach (var rule in config.EntryRules)
            {
                 bool match = eval.Evaluate(rule, ctx, "TEST", (msg) => Console.WriteLine(msg));
                 Console.WriteLine($"Direct Evaluator Result: {match}");
            }
            Console.WriteLine("=================================================");

            Console.WriteLine($"Total Signals: {signals}");
        }
    }
}
