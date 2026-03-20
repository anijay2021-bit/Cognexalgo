namespace Cognexalgo.Core.Application.Interfaces
{
    /// <summary>
    /// Encapsulates all position-sizing and trade-gate logic for the VCP strategy.
    /// Pure calculation — no broker calls, no I/O.
    /// </summary>
    public interface IVCPRiskManager
    {
        /// <summary>
        /// Calculates the number of lots to trade so that the maximum loss on the position
        /// (if stop-loss is hit immediately) does not exceed <paramref name="riskAmountPerTrade"/>.
        /// </summary>
        /// <param name="entryPrice">The planned entry price per unit/share.</param>
        /// <param name="stopLoss">The initial stop-loss price per unit/share.</param>
        /// <param name="riskAmountPerTrade">
        /// Maximum capital to risk on this trade in rupees (e.g. ₹1 000).
        /// </param>
        /// <param name="lotSize">
        /// Number of units per lot for the instrument (e.g. 75 for NIFTY options).
        /// </param>
        /// <returns>
        /// The number of whole lots that keeps the risk within <paramref name="riskAmountPerTrade"/>.
        /// Returns 0 when the stop-loss distance is zero or the risk budget cannot cover one lot.
        /// </returns>
        int CalculateLots(decimal entryPrice, decimal stopLoss,
                          decimal riskAmountPerTrade, int lotSize);

        /// <summary>
        /// Determines whether a new VCP trade may be opened based on the current
        /// number of concurrent open trades and the configured limit.
        /// </summary>
        /// <param name="currentOpenTrades">Number of VCP positions currently open.</param>
        /// <param name="maxAllowedTrades">
        /// The maximum concurrent trades permitted (from <c>VCPSettings.MaxConcurrentTrades</c>).
        /// </param>
        /// <returns>
        /// <c>true</c> when <paramref name="currentOpenTrades"/> is strictly less than
        /// <paramref name="maxAllowedTrades"/>; <c>false</c> otherwise.
        /// </returns>
        bool CanOpenTrade(int currentOpenTrades, int maxAllowedTrades);

        /// <summary>
        /// Calculates the total capital deployed for a position in rupees.
        /// </summary>
        /// <param name="lots">Number of lots to trade.</param>
        /// <param name="lotSize">Units per lot.</param>
        /// <param name="premium">Price per unit (options premium or share price).</param>
        /// <returns>
        /// Total rupee value of the position: <c>lots × lotSize × premium</c>.
        /// </returns>
        decimal CalculatePositionSize(int lots, int lotSize, decimal premium);
    }
}
