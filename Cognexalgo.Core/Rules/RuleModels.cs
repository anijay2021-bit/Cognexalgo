using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Cognexalgo.Core.Rules
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum IndicatorType
    {
        LTP,
        EMA,
        SMA,
        RSI,
        MACD,
        SUPERTREND,
        BOLLINGER_BANDS,
        VWAP,
        ATR
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum Comparator
    {
        GREATER_THAN,
        LESS_THAN,
        EQUALS,
        CROSS_ABOVE,
        CROSS_BELOW,
        CLOSES_ABOVE,
        CLOSES_BELOW
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum ValueSource
    {
        StaticValue,
        Indicator,
        TrendFilter
    }

    public class Condition
    {
        // Left Side
        public IndicatorType Indicator { get; set; }
        public int Period { get; set; } // For Moving Averages/RSI
        public int Multiplier { get; set; } // For SuperTrend
        
        // Operator
        public Comparator Operator { get; set; }

        // Right Side
        public ValueSource SourceType { get; set; } // Compare against a Number (70) or another Indicator (EMA 20)
        public double StaticValue { get; set; }
        public IndicatorType RightIndicator { get; set; }
        public int RightPeriod { get; set; }
    }

    public class Rule
    {
        public System.Collections.Generic.List<Condition> Conditions { get; set; } = new System.Collections.Generic.List<Condition>();
        public string Action { get; set; } // "BUY_CE", "BUY_PE", "EXIT"
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum TargetType
    {
        Percentage,
        AbsolutePoints,
        ATR,
        SignalBased // For standard exit rules
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum StopLossType
    {
        Percentage,
        AbsolutePoints,
        ATR,
        CandleClose
    }

    public class ExitConfig
    {
        public TargetType TargetType { get; set; } = TargetType.Percentage;
        public double TargetValue { get; set; } // 1.5, 100, etc.

        public StopLossType StopLossType { get; set; } = StopLossType.Percentage;
        public double StopLossValue { get; set; } // 1.0, 50, etc.

        public int AtrPeriod { get; set; } = 14;
        public double AtrMultiplier { get; set; } = 2.0;

        // Phase 1: Trailing Stop Loss
        public bool TrailingStopLoss { get; set; }
        public double TrailingStopDistance { get; set; } = 1.0; // % or points
        public bool TrailingStopIsPercent { get; set; } = true;

        // Phase 1: Time-Based Exit
        public bool EnableTimeBasedExit { get; set; } = false;
        public string ExitTime { get; set; } = "15:15"; // HH:mm format

        // Phase 1: Breakeven Stop
        public bool EnableBreakevenStop { get; set; } = false;
        public double BreakevenTriggerPercent { get; set; } = 1.0; // Move to BE at +1%

        // Phase 2: Partial Exits
        public bool EnablePartialExits { get; set; } = false;
        public List<PartialExitLevel> PartialExitLevels { get; set; } = new List<PartialExitLevel>();

        // Phase 2: Profit Protection
        public bool EnableProfitProtection { get; set; } = false;
        public double ProfitProtectionPercent { get; set; } = 50.0; // Protect 50% of gain
        public double ProfitProtectionTrigger { get; set; } = 2.0; // Activate at +2%
    }

    public class PartialExitLevel
    {
        public double Percentage { get; set; } // % of position to exit
        public double TargetPercent { get; set; } // Price target %
        public bool IsExited { get; set; } // Tracking field for strategy
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum ExpiryType
    {
        Weekly,
        Monthly
    }

    public class ResilientRuleListConverter : JsonConverter<List<Rule>>
    {
        public override List<Rule> ReadJson(JsonReader reader, Type objectType, List<Rule> existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String)
            {
                string json = (string)reader.Value;
                if (string.IsNullOrEmpty(json) || json == "[]") return new List<Rule>();
                try {
                    return JsonConvert.DeserializeObject<List<Rule>>(json);
                } catch {
                    return new List<Rule>();
                }
            }
            
            if (reader.TokenType == JsonToken.Null) return new List<Rule>();
            
            try {
                var JArray = Newtonsoft.Json.Linq.JArray.Load(reader);
                return JArray.ToObject<List<Rule>>();
            } catch {
                return new List<Rule>();
            }
        }

        public override void WriteJson(JsonWriter writer, List<Rule> value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, value);
        }
    }

    public class DynamicStrategyConfig
    {
        public string StrategyName { get; set; }
        public string Symbol { get; set; } // "NIFTY", "BANKNIFTY"
        public string Timeframe { get; set; } // "5min"
        
        public ExpiryType ExpiryType { get; set; } = ExpiryType.Weekly; // Default

        public string ProductType { get; set; } = "MIS"; // "MIS" or "NRML"

        public int TotalLots { get; set; } = 1; // Default to 1 lot

        // Logic: N Rules. 
        // If "Match All", UI sends 1 Rule with N Conditions.
        // If "Match Any", UI sends N Rules with 1 Condition each.
        [JsonConverter(typeof(ResilientRuleListConverter))]
        public System.Collections.Generic.List<Rule> EntryRules { get; set; } = new System.Collections.Generic.List<Rule>();
        
        // Standard signal-based exits (e.g., RSI > 80)
        [JsonConverter(typeof(ResilientRuleListConverter))]
        public System.Collections.Generic.List<Rule> ExitRules { get; set; } = new System.Collections.Generic.List<Rule>();
        
        // Advanced risk-based exits
        public ExitConfig ExitSettings { get; set; } = new ExitConfig();
    }
}
