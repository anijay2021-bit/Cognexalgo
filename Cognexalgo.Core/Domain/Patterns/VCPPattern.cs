using System;
using System.Collections.Generic;
using System.Linq;

namespace Cognexalgo.Core.Domain.Patterns
{
    /// <summary>
    /// Represents a fully detected Volatility Contraction Pattern (VCP) on a given symbol.
    /// A VCP consists of a series of progressively tighter price contractions with declining
    /// volume, culminating in a pivot breakout level.
    /// </summary>
    public class VCPPattern
    {
        /// <summary>
        /// The underlying equity or index symbol on which the pattern was detected (e.g. "RELIANCE").
        /// </summary>
        public string Symbol { get; init; } = string.Empty;

        /// <summary>
        /// The ordered list of contraction phases, from earliest (largest) to latest (tightest).
        /// </summary>
        public List<Contraction> Contractions { get; init; } = new();

        /// <summary>
        /// The breakout pivot level — the swing high of the final (tightest) contraction.
        /// A close or intraday move above this level constitutes a valid VCP breakout entry.
        /// </summary>
        public decimal PivotLevel { get; init; }

        /// <summary>
        /// The swing low of the final (tightest) contraction.
        /// Used as the basis for the initial stop-loss placement.
        /// </summary>
        public decimal TightLow { get; init; }

        /// <summary>
        /// The timestamp at which the pattern was identified by the scanner.
        /// </summary>
        public DateTime DetectedAt { get; init; }

        /// <summary>
        /// The chart timeframe on which the pattern was scanned (e.g. "Daily", "15min").
        /// </summary>
        public string Timeframe { get; init; } = string.Empty;

        /// <summary>
        /// The overall quality grade of this VCP instance.
        /// </summary>
        public VCPQuality Quality { get; init; }

        /// <summary>
        /// Returns <c>true</c> when the pattern satisfies all structural VCP rules:
        /// <list type="bullet">
        ///   <item>Between 2 and 5 contractions (inclusive)</item>
        ///   <item>Each contraction's SwingPercent is strictly smaller than the preceding one</item>
        ///   <item>Final contraction AvgVolume is less than 65% of the first contraction AvgVolume</item>
        /// </list>
        /// Never throws — returns <c>false</c> on any null or empty contraction list.
        /// </summary>
        public bool IsValid
        {
            get
            {
                if (Contractions is not { Count: >= 2 and <= 5 })
                    return false;

                // Each contraction must be strictly tighter than the previous
                for (int i = 1; i < Contractions.Count; i++)
                {
                    if (Contractions[i].SwingPercent >= Contractions[i - 1].SwingPercent)
                        return false;
                }

                // Final contraction volume must be below 65% of the first contraction volume
                long firstVolume = Contractions[0].AvgVolume;
                long lastVolume  = Contractions[^1].AvgVolume;

                if (firstVolume <= 0)
                    return false;

                return lastVolume < firstVolume * 0.65m;
            }
        }

        /// <summary>
        /// The number of contraction phases in this pattern.
        /// </summary>
        public int ContractionCount => Contractions?.Count ?? 0;

        /// <summary>
        /// The SwingPercent of the final (tightest) contraction.
        /// Represents how tight the last consolidation is as a percentage of its swing high.
        /// Returns 0 if no contractions are present.
        /// </summary>
        public decimal TightRangePercent => Contractions?.Count > 0
            ? Contractions[^1].SwingPercent
            : 0m;
    }
}
