using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Npgsql;
using Newtonsoft.Json;
using Cognexalgo.Core.Models;
using Cognexalgo.Core.Rules;

namespace TempQuery
{
    class Program
    {
        static async Task Main(string[] args)
        {
            string connStr = "Host=aws-1-ap-southeast-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.dcsjwozwltcixdlgzalr;Password=3GTeWMvIwGBHMQXd;SSL Mode=Require;Trust Server Certificate=true;Command Timeout=60;";
            try 
            {
                using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync();

                // ------------------ 21CE Configuration ------------------
                var ceRules = new List<Rule>
                {
                    new Rule {
                        Action = "BUY_CE",
                        Conditions = new List<Condition> {
                            new Condition { Indicator = IndicatorType.LTP, Period = 1, Operator = Comparator.CROSS_ABOVE, SourceType = ValueSource.Indicator, RightIndicator = IndicatorType.EMA, RightPeriod = 21 }
                        }
                    }
                };
                
                var ceParamsDict = new Dictionary<string, string>
                {
                    { "Symbol", "NIFTY" },
                    { "Timeframe", "1min" },
                    { "IsMatchAllConditions", "True" },
                    { "SelectedTargetType", "Percentage" },
                    { "TargetValue", "1" },
                    { "SelectedStopLossType", "Percentage" },
                    { "StopLossValue", "1" },
                    { "EntryRules", JsonConvert.SerializeObject(ceRules) },
                    { "ExitRules", "[]" }
                };

                var ceConfig = new HybridStrategyConfig
                {
                    Name = "21ce",
                    IsActive = true,
                    ProductType = "MIS",
                    ExpiryType = "Weekly",
                    StrategyType = "CUSTOM",
                    Legs = new List<StrategyLeg>(), // Dynamic logic doesn't use static legs collection
                    Parameters = JsonConvert.SerializeObject(ceParamsDict),
                    AutoExecute = true,
                    MaxProfitPercent = 1,
                    MaxLossPercent = 1
                };

                // ------------------ 21PE Configuration ------------------
                var peRules = new List<Rule>
                {
                    new Rule {
                        Action = "BUY_PE",
                        Conditions = new List<Condition> {
                            new Condition { Indicator = IndicatorType.LTP, Period = 1, Operator = Comparator.CROSS_BELOW, SourceType = ValueSource.Indicator, RightIndicator = IndicatorType.EMA, RightPeriod = 21 }
                        }
                    }
                };
                
                var peParamsDict = new Dictionary<string, string>
                {
                    { "Symbol", "NIFTY" },
                    { "Timeframe", "1min" },
                    { "IsMatchAllConditions", "True" },
                    { "SelectedTargetType", "Percentage" },
                    { "TargetValue", "1" },
                    { "SelectedStopLossType", "Percentage" },
                    { "StopLossValue", "1" },
                    { "EntryRules", JsonConvert.SerializeObject(peRules) },
                    { "ExitRules", "[]" }
                };

                var peConfig = new HybridStrategyConfig
                {
                    Name = "21PE",
                    IsActive = true,
                    ProductType = "MIS",
                    ExpiryType = "Weekly",
                    StrategyType = "CUSTOM",
                    Legs = new List<StrategyLeg>(), // Dynamic logic doesn't use static legs collection
                    Parameters = JsonConvert.SerializeObject(peParamsDict),
                    AutoExecute = true,
                    MaxProfitPercent = 1,
                    MaxLossPercent = 1
                };

                // Insert into DB exactly as EF Core would serialize HybridStrategyConfig
                string jsonCE = JsonConvert.SerializeObject(ceConfig);
                string jsonPE = JsonConvert.SerializeObject(peConfig);

                string sql = @"
                    INSERT INTO hybrid_strategies (""Name"", ""ConfigJson"", ""IsActive"", ""CreatedAt"", ""LastModified"", ""CreatedBy"", ""LastModifiedBy"", ""Version"")
                    VALUES 
                    ('21ce', @jsonCE, true, NOW(), NOW(), 'System', 'System', 1),
                    ('21PE', @jsonPE, true, NOW(), NOW(), 'System', 'System', 1)
                    ON CONFLICT (""Name"") 
                    DO UPDATE SET ""ConfigJson"" = EXCLUDED.""ConfigJson"", ""LastModified"" = NOW();
                ";

                using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("jsonCE", jsonCE);
                cmd.Parameters.AddWithValue("jsonPE", jsonPE);
                
                int rows = await cmd.ExecuteNonQueryAsync();
                Console.WriteLine($"Successfully saved settings into database. Rows affected: {rows}");
            } 
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }
    }
}
