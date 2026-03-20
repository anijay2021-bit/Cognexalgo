using Cognexalgo.Core.Models;

namespace Cognexalgo.Core.Application.Interfaces
{
    /// <summary>
    /// Determines which broker mode is active for the VCP strategy.
    /// Implementations return the resolved mode based on configuration or runtime flags.
    /// </summary>
    public interface IBrokerFactory
    {
        /// <summary>
        /// Resolves the effective trading mode.
        /// Implementations may override <paramref name="requestedMode"/> (e.g. force paper
        /// during market hours validation) and return the actual mode to use.
        /// </summary>
        VCPTradingMode GetActiveMode(VCPTradingMode requestedMode);
    }
}
