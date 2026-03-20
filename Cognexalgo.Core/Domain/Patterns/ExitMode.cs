namespace Cognexalgo.Core.Domain.Patterns
{
    /// <summary>
    /// Controls when an exit order is submitted after an exit condition is triggered.
    /// </summary>
    public enum ExitMode
    {
        /// <summary>
        /// Submit a market exit order immediately when the trigger condition fires,
        /// regardless of whether the current candle has closed.
        /// Use this for stop-loss hits or hard risk limits.
        /// </summary>
        Immediate,

        /// <summary>
        /// Wait for the current candle to close before submitting the exit order.
        /// Reduces false exits from intra-candle wicks; preferred for target exits
        /// or pattern-failure signals on higher timeframes.
        /// </summary>
        WaitForCandleClose
    }
}
