using System.Collections.Generic;
using Cognexalgo.Core.Domain.Patterns;
using Cognexalgo.Core.Models;

namespace Cognexalgo.Core.Application.Interfaces
{
    /// <summary>
    /// Detects VCP (Volatility Contraction Pattern) structures in a candle series
    /// and evaluates real-time breakout and failure conditions.
    /// Implementations must be stateless — all context is passed via parameters.
    /// </summary>
    public interface IVCPDetector
    {
        /// <summary>
        /// Scans a chronologically ordered candle series for a VCP pattern.
        /// </summary>
        /// <param name="candles">
        /// Ordered list of candles (oldest first). Minimum 20 candles recommended
        /// for reliable contraction identification.
        /// </param>
        /// <param name="symbol">
        /// The underlying symbol (e.g. "RELIANCE"). Stored on the returned pattern.
        /// </param>
        /// <param name="timeframe">
        /// The chart timeframe string (e.g. "Daily", "15min"). Stored on the returned pattern.
        /// </param>
        /// <returns>
        /// A <see cref="VCPPattern"/> when a valid pattern is found;
        /// <c>null</c> when no VCP structure is present in the candle series.
        /// </returns>
        VCPPattern? Detect(List<Candle> candles, string symbol, string timeframe);

        /// <summary>
        /// Determines whether the current candle constitutes a valid VCP breakout.
        /// A breakout is confirmed when price trades above <see cref="VCPPattern.PivotLevel"/>
        /// and volume is elevated relative to the average of the final contraction.
        /// </summary>
        /// <param name="pattern">The previously detected VCP pattern to evaluate against.</param>
        /// <param name="currentCandle">The latest candle (may be incomplete/in-progress).</param>
        /// <returns>
        /// <c>true</c> when price is above the pivot level with confirming volume;
        /// <c>false</c> otherwise.
        /// </returns>
        bool IsBreakingOut(VCPPattern pattern, Candle currentCandle);

        /// <summary>
        /// Determines whether the VCP pattern has structurally failed.
        /// Failure is defined as price trading below <see cref="VCPPattern.TightLow"/>,
        /// invalidating the base and requiring immediate exit of any open position.
        /// </summary>
        /// <param name="pattern">The active VCP pattern being monitored.</param>
        /// <param name="currentCandle">The latest candle to evaluate.</param>
        /// <returns>
        /// <c>true</c> when price has broken below the tight low; <c>false</c> otherwise.
        /// </returns>
        bool IsPatternFailed(VCPPattern pattern, Candle currentCandle);

        /// <summary>
        /// Identifies whether the current candle shows a bearish reversal signal
        /// relative to the preceding candle (e.g. bearish engulfing, shooting star,
        /// or strong close below the midpoint of the prior candle).
        /// </summary>
        /// <param name="currentCandle">The candle to assess for reversal characteristics.</param>
        /// <param name="previousCandle">
        /// The candle immediately preceding <paramref name="currentCandle"/>, used as context
        /// for pattern comparisons such as engulfing.
        /// </param>
        /// <returns>
        /// <c>true</c> when a bearish reversal candlestick pattern is identified;
        /// <c>false</c> otherwise.
        /// </returns>
        bool IsReversalCandle(Candle currentCandle, Candle previousCandle);
    }
}
