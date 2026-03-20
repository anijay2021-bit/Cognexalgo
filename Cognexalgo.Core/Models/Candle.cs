using System;

namespace Cognexalgo.Core.Models
{
    /// <summary>
    /// Pure domain candle — the unit of OHLCV price data consumed by strategies and detectors.
    /// This is a clean domain model with no persistence annotations.
    /// For the LiteDB-cached equivalent see <c>Cognexalgo.Core.Data.Entities.CachedCandle</c>.
    /// </summary>
    public class Candle
    {
        /// <summary>
        /// The underlying equity or index symbol (e.g. "NIFTY", "RELIANCE").
        /// </summary>
        public string Symbol { get; init; } = string.Empty;

        /// <summary>Opening price of the candle period.</summary>
        public decimal Open { get; init; }

        /// <summary>Highest price reached during the candle period.</summary>
        public decimal High { get; init; }

        /// <summary>Lowest price reached during the candle period.</summary>
        public decimal Low { get; init; }

        /// <summary>Closing price of the candle period.</summary>
        public decimal Close { get; init; }

        /// <summary>
        /// Total traded volume during the candle period, in shares or contracts.
        /// </summary>
        public long Volume { get; init; }

        /// <summary>
        /// The opening timestamp of this candle (exchange local time).
        /// For daily candles this is the session open date at 09:15.
        /// </summary>
        public DateTime Timestamp { get; init; }

        /// <summary>
        /// The chart timeframe this candle belongs to (e.g. "Daily", "15min", "1min").
        /// Matches the string used throughout the application for timeframe identification.
        /// </summary>
        public string Timeframe { get; init; } = string.Empty;

        /// <summary>
        /// Returns <c>true</c> when the candle closed above its open (bullish body).
        /// </summary>
        public bool IsBullish => Close > Open;

        /// <summary>
        /// Returns <c>true</c> when the candle closed below its open (bearish body).
        /// </summary>
        public bool IsBearish => Close < Open;

        /// <summary>
        /// Body size as a percentage of the candle's total range (High − Low).
        /// Returns 0 when the candle has no range to avoid division-by-zero.
        /// </summary>
        public decimal BodyPercent => (High - Low) == 0
            ? 0
            : Math.Round(Math.Abs(Close - Open) / (High - Low) * 100, 2);

        public override string ToString() =>
            $"{Symbol} [{Timeframe}] {Timestamp:dd-MMM-yy HH:mm} O:{Open} H:{High} L:{Low} C:{Close} V:{Volume}";
    }
}
