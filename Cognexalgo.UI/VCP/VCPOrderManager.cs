using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Cognexalgo.Core.Application.Interfaces;
using Cognexalgo.Core.Models;

namespace Cognexalgo.UI.VCP
{
    /// <summary>
    /// Routes VCP orders to the appropriate execution path (paper sim or live broker).
    ///
    /// Paper mode:  order is confirmed immediately with a generated order ID;
    ///              no external call is made — the strategy drives P&amp;L internally.
    /// Live mode:   not yet wired — returns <c>null</c> with a diagnostic trace until
    ///              the Angel One order placement path is fully integrated.
    /// </summary>
    internal sealed class VCPOrderManager : IOrderManagementService
    {
        private readonly IVCPSettingsService _settingsService;

        public VCPOrderManager(IVCPSettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        /// <inheritdoc/>
        public Task<string?> PlaceOrderAsync(VCPOrder order, CancellationToken ct = default)
        {
            // Use the mode embedded in the order itself (set by VCPOptionsStrategy).
            if (order.Mode == VCPTradingMode.PaperTrade)
                return Task.FromResult<string?>(SimulatePaperOrder(order));

            // Live trade path — log and return null (not yet implemented).
            Debug.WriteLine(
                $"[VCPOrderManager] LIVE order requested but not yet wired: " +
                $"{order.TransactionType} {order.TradingSymbol} qty={order.Quantity}");

            return Task.FromResult<string?>(null);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string SimulatePaperOrder(VCPOrder order)
        {
            var orderId = $"PAPER-{Guid.NewGuid():N}";

            Debug.WriteLine(
                $"[VCPOrderManager] Paper order confirmed: {order.TransactionType} " +
                $"{order.TradingSymbol} qty={order.Quantity} " +
                $"price={order.Price:N0} | orderId={orderId}");

            return orderId;
        }
    }
}
