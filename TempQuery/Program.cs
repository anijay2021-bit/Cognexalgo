using System;
using System.Threading.Tasks;
using Npgsql;

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

                string json21ce = @"{
  ""StrategyName"": ""21ce"",
  ""Symbol"": ""NIFTY"",
  ""Timeframe"": ""1min"",
  ""ExpiryType"": ""Weekly"",
  ""ProductType"": ""MIS"",
  ""TotalLots"": 1,
  ""EntryRules"": [
    {
      ""Conditions"": [
        {
          ""Indicator"": ""LTP"",
          ""Period"": 1,
          ""Multiplier"": 0,
          ""Operator"": ""CROSS_ABOVE"",
          ""SourceType"": ""Indicator"",
          ""StaticValue"": 0.0,
          ""RightIndicator"": ""EMA"",
          ""RightPeriod"": 21
        }
      ],
      ""Action"": ""BUY_CE""
    }
  ],
  ""ExitRules"": [],
  ""ExitSettings"": {
    ""TargetType"": ""Percentage"",
    ""TargetValue"": 0.0,
    ""StopLossType"": ""Percentage"",
    ""StopLossValue"": 0.0,
    ""AtrPeriod"": 14,
    ""AtrMultiplier"": 2.0,
    ""TrailingStopLoss"": false,
    ""TrailingStopDistance"": 1.0,
    ""TrailingStopIsPercent"": true,
    ""EnableTimeBasedExit"": false,
    ""EnableBreakevenStop"": false,
    ""EnablePartialExits"": false,
    ""EnableProfitProtection"": false
  }
}";

                string json21pe = @"{
  ""StrategyName"": ""21PE"",
  ""Symbol"": ""NIFTY"",
  ""Timeframe"": ""1min"",
  ""ExpiryType"": ""Weekly"",
  ""ProductType"": ""MIS"",
  ""TotalLots"": 1,
  ""EntryRules"": [
    {
      ""Conditions"": [
        {
          ""Indicator"": ""LTP"",
          ""Period"": 1,
          ""Multiplier"": 0,
          ""Operator"": ""CROSS_BELOW"",
          ""SourceType"": ""Indicator"",
          ""StaticValue"": 0.0,
          ""RightIndicator"": ""EMA"",
          ""RightPeriod"": 21
        }
      ],
      ""Action"": ""BUY_PE""
    }
  ],
  ""ExitRules"": [],
  ""ExitSettings"": {
    ""TargetType"": ""Percentage"",
    ""TargetValue"": 0.0,
    ""StopLossType"": ""Percentage"",
    ""StopLossValue"": 0.0,
    ""AtrPeriod"": 14,
    ""AtrMultiplier"": 2.0,
    ""TrailingStopLoss"": false,
    ""TrailingStopDistance"": 1.0,
    ""TrailingStopIsPercent"": true,
    ""EnableTimeBasedExit"": false,
    ""EnableBreakevenStop"": false,
    ""EnablePartialExits"": false,
    ""EnableProfitProtection"": false
  }
}";

                string sql = @"
                    INSERT INTO hybrid_strategies (""Name"", ""ConfigJson"", ""IsActive"", ""CreatedAt"", ""LastModified"", ""CreatedBy"", ""LastModifiedBy"", ""Version"")
                    VALUES 
                    ('21ce', @jsonCE, true, NOW(), NOW(), 'System', 'System', 1),
                    ('21PE', @jsonPE, true, NOW(), NOW(), 'System', 'System', 1)
                    ON CONFLICT (""Name"") 
                    DO UPDATE SET ""ConfigJson"" = EXCLUDED.""ConfigJson"", ""LastModified"" = NOW();
                ";

                using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("jsonCE", json21ce);
                cmd.Parameters.AddWithValue("jsonPE", json21pe);
                
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
