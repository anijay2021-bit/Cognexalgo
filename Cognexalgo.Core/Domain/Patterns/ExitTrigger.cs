namespace Cognexalgo.Core.Domain.Patterns
{
    /// <summary>
    /// Identifies the reason a VCP trade was closed.
    /// Used for post-trade analytics and rule-based exit routing.
    /// </summary>
    public enum ExitTrigger
    {
        /// <summary>
        /// The price breached the pre-defined stop-loss level.
        /// </summary>
        StopLossHit,

        /// <summary>
        /// The first profit target was reached (typically 1:1 or partial exit level).
        /// </summary>
        Target1Hit,

        /// <summary>
        /// The second profit target was reached (full exit or trailing stop activation level).
        /// </summary>
        Target2Hit,

        /// <summary>
        /// Position was squared off at end-of-day to avoid overnight/expiry risk.
        /// </summary>
        EndOfDaySquareOff,

        /// <summary>
        /// The underlying VCP structure was invalidated (e.g. close below pivot or base).
        /// </summary>
        PatternFailure,

        /// <summary>
        /// The trader exited the position manually via the dashboard.
        /// </summary>
        ManualExit
    }
}
