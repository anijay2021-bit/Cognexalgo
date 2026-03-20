using System;

namespace Cognexalgo.Core.Domain.Patterns
{
    /// <summary>
    /// Represents a single price contraction (tightening) phase in a VCP pattern.
    /// Each contraction is defined by a swing high and swing low within a discrete time window.
    /// </summary>
    public class Contraction
    {
        /// <summary>
        /// The highest price reached during this contraction phase.
        /// </summary>
        public decimal SwingHigh { get; init; }

        /// <summary>
        /// The lowest price reached during this contraction phase.
        /// </summary>
        public decimal SwingLow { get; init; }

        /// <summary>
        /// The price range of this contraction expressed as a percentage of the swing high.
        /// Returns 0 if SwingHigh is zero (prevents division-by-zero).
        /// </summary>
        public decimal SwingPercent => SwingHigh == 0
            ? 0
            : Math.Round((SwingHigh - SwingLow) / SwingHigh * 100, 2);

        /// <summary>
        /// Average daily volume during this contraction phase, in shares/contracts.
        /// </summary>
        public long AvgVolume { get; init; }

        /// <summary>
        /// The date on which this contraction phase began.
        /// </summary>
        public DateTime StartDate { get; init; }

        /// <summary>
        /// The date on which this contraction phase ended (i.e., pivot or re-expansion started).
        /// </summary>
        public DateTime EndDate { get; init; }
    }
}
