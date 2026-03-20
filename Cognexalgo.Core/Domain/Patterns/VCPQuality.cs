namespace Cognexalgo.Core.Domain.Patterns
{
    /// <summary>
    /// Quality grade of a detected VCP (Volatility Contraction Pattern).
    /// Graded A–C based on contraction symmetry, volume behaviour, and tightness.
    /// Invalid is assigned when structural requirements are not met.
    /// </summary>
    public enum VCPQuality
    {
        /// <summary>
        /// Textbook VCP: 3–4 contractions, clean volume dry-up, tight final pivot (≤ 3%).
        /// </summary>
        A,

        /// <summary>
        /// Good VCP: 2–4 contractions, acceptable volume trend, final pivot ≤ 5%.
        /// </summary>
        B,

        /// <summary>
        /// Marginal VCP: minimum requirements met but noisy contractions or moderate volume.
        /// </summary>
        C,

        /// <summary>
        /// Pattern does not meet the minimum structural criteria for a valid VCP.
        /// </summary>
        Invalid
    }
}
