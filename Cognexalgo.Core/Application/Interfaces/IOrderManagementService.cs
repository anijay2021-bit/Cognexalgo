using System.Threading;
using System.Threading.Tasks;
using Cognexalgo.Core.Models;

namespace Cognexalgo.Core.Application.Interfaces
{
    /// <summary>
    /// Routes buy and sell orders to the appropriate broker (paper or live),
    /// based on the <see cref="VCPOrder.Mode"/> carried in each order.
    /// Pure order dispatch — no position tracking.
    /// </summary>
    public interface IOrderManagementService
    {
        /// <summary>
        /// Places an order and returns the broker-assigned order ID.
        /// Returns <c>null</c> when the order was rejected.
        /// Throws only on unrecoverable communication errors — callers must handle exceptions.
        /// </summary>
        Task<string?> PlaceOrderAsync(VCPOrder order, CancellationToken ct = default);
    }
}
