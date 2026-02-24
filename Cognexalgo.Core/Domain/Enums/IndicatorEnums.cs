namespace Cognexalgo.Core.Domain.Enums
{
    /// <summary>
    /// All supported indicator types across Trend, Momentum, Volatility, Volume categories.
    /// </summary>
    public enum IndicatorType
    {
        // Trend
        EMA,
        SMA,
        SuperTrend,
        VWAP,
        BollingerBands,

        // Momentum
        RSI,
        MACD,
        Stochastic,
        CCI,

        // Volatility
        ATR,
        IndiaVIX,

        // Volume
        VolumeSMA,
        OBV,

        // Special
        LTP,       // Last Traded Price (current price)
        StaticValue
    }

    /// <summary>
    /// Comparison operators for signal conditions.
    /// </summary>
    public enum Comparator
    {
        CrossesAbove,
        CrossesBelow,
        IsAbove,
        IsBelow,
        IsEqual,
        IncreasesBy,
        DecreasesBy,
        IsOverbought,
        IsOversold,
        // Legacy compat
        CROSS_ABOVE,
        CROSS_BELOW,
        GREATER_THAN,
        LESS_THAN,
        CLOSES_ABOVE,
        CLOSES_BELOW
    }

    /// <summary>
    /// Supported timeframes for indicator calculation.
    /// </summary>
    public enum TimeFrame
    {
        Min1,
        Min3,
        Min5,
        Min10,
        Min15,
        Min30,
        Hour1,
        Day1
    }

    /// <summary>
    /// Logic operators for combining condition groups.
    /// </summary>
    public enum LogicOperator
    {
        AND,
        OR
    }

    /// <summary>
    /// Source of the right-hand value in a condition.
    /// </summary>
    public enum ValueSource
    {
        Indicator,
        StaticValue,
        TrendFilter
    }
}
