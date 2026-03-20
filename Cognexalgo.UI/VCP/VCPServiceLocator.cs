using Microsoft.Extensions.Logging;
using Cognexalgo.Core;
using Cognexalgo.Core.Application.Interfaces;
using Cognexalgo.Core.Services.Detection;
using Cognexalgo.Core.Services.Risk;
using Cognexalgo.Core.Services.Settings;
using Cognexalgo.Core.Services.Strategy;
using Cognexalgo.UI.ViewModels;

namespace Cognexalgo.UI.VCP
{
    /// <summary>
    /// Manual composition root for all VCP components.
    ///
    /// This is NOT a DI container — it follows the project's existing pattern of manually
    /// newing dependencies in dependency order. One instance is created per application
    /// session and held by <see cref="ViewModels.MainViewModel"/>.
    /// </summary>
    internal sealed class VCPServiceLocator
    {
        // Shared logger factory with no providers — logs are discarded silently.
        // The UI project does not reference a logging provider package; add one
        // (e.g. Microsoft.Extensions.Logging.Debug) to see VCP diagnostic output.
        private static readonly ILoggerFactory _loggerFactory =
            LoggerFactory.Create(_ => { });

        // ── Public surface ────────────────────────────────────────────────────

        public IVCPSettingsService    SettingsService   { get; }
        public IMarketDataService     MarketData        { get; }
        public IVCPStrategy           Strategy          { get; }

        public VCPSettingsViewModel   SettingsViewModel { get; }
        public VCPScannerViewModel    ScannerViewModel  { get; }

        // ── Constructor ───────────────────────────────────────────────────────

        public VCPServiceLocator(TradingEngine engine)
        {
            // ── Leaf services (no mutual dependencies) ────────────────────────
            var detector    = new VCPDetector(
                                  _loggerFactory.CreateLogger<VCPDetector>());

            var riskManager = new VCPRiskManager(
                                  _loggerFactory.CreateLogger<VCPRiskManager>());

            SettingsService = new VCPSettingsService(
                                  _loggerFactory.CreateLogger<VCPSettingsService>());

            // ── Infrastructure adapters ───────────────────────────────────────
            MarketData = new TradingEngineMarketDataAdapter(engine);

            var brokerModeResolver = new VCPBrokerModeResolver(SettingsService);
            var orderManager       = new VCPOrderManager(SettingsService);

            // ── Strategy (wires all leaf services) ────────────────────────────
            Strategy = new VCPOptionsStrategy(
                           detector,
                           riskManager,
                           SettingsService,
                           MarketData,
                           orderManager,
                           brokerModeResolver,
                           _loggerFactory.CreateLogger<VCPOptionsStrategy>());

            // ── ViewModels ────────────────────────────────────────────────────
            SettingsViewModel = new VCPSettingsViewModel(SettingsService, Strategy);
            ScannerViewModel  = new VCPScannerViewModel(Strategy, SettingsService, MarketData);
        }
    }
}
