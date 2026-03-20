using Cognexalgo.Core.Application.Interfaces;
using Cognexalgo.Core.Models;

namespace Cognexalgo.UI.VCP
{
    /// <summary>
    /// Resolves the effective VCP trading mode by delegating to the persisted settings.
    /// Implements <see cref="IBrokerFactory"/> as required by <c>VCPOptionsStrategy</c>.
    ///
    /// This is a thin mode resolver, not a broker factory. The name "IBrokerFactory" in
    /// Cognexalgo.Core refers to trading-mode resolution, not broker instantiation.
    /// </summary>
    internal sealed class VCPBrokerModeResolver : IBrokerFactory
    {
        private readonly IVCPSettingsService _settingsService;

        public VCPBrokerModeResolver(IVCPSettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        /// <inheritdoc/>
        public VCPTradingMode GetActiveMode(VCPTradingMode requestedMode)
        {
            // Honor whatever mode the caller requests.
            // A future implementation could force PaperTrade outside market hours.
            return requestedMode;
        }
    }
}
