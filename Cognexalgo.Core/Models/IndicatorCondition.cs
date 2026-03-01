namespace Cognexalgo.Core.Models
{
    /// <summary>
    /// F2: One entry condition based on a technical indicator.
    /// All conditions on a leg must be satisfied before entry is allowed.
    /// </summary>
    public class IndicatorCondition
    {
        /// <summary>Indicator name matching IndicatorType enum. E.g. "RSI", "EMA", "VWAP", "SuperTrend".</summary>
        public string IndicatorType { get; set; } = "RSI";

        /// <summary>Indicator period. E.g. 14 for RSI(14), 20 for EMA(20).</summary>
        public int Period { get; set; } = 14;

        /// <summary>Timeframe matching TimeFrame enum. E.g. "Min15", "Min5", "Hour1", "Day1".</summary>
        public string TimeFrame { get; set; } = "Min15";

        /// <summary>Comparison operator: "&lt;" | "&gt;" | "&lt;=" | "&gt;=" | "==" </summary>
        public string Comparator { get; set; } = "<";

        /// <summary>Threshold value to compare indicator value against.</summary>
        public double Value { get; set; } = 30;
    }
}
