using System;
using Microsoft.Extensions.Logging;
using Cognexalgo.Core.Application.Interfaces;

namespace Cognexalgo.Core.Services.Risk
{
    /// <summary>
    /// Pure position-sizing and trade-gate logic for the VCP strategy.
    /// Stateless — safe to register as singleton.
    /// </summary>
    public sealed class VCPRiskManager : IVCPRiskManager
    {
        private readonly ILogger<VCPRiskManager> _logger;

        private const int MaxLots = 10;
        private const int MaxAllowedTradesCap = 4;

        public VCPRiskManager(ILogger<VCPRiskManager> logger)
        {
            _logger = logger;
        }

        /// <inheritdoc/>
        public int CalculateLots(decimal entryPrice, decimal stopLoss,
                                 decimal riskAmountPerTrade, int lotSize)
        {
            if (entryPrice <= 0 || riskAmountPerTrade <= 0 || lotSize <= 0)
            {
                _logger.LogWarning(
                    "[VCPRiskManager] CalculateLots called with invalid input: " +
                    "entryPrice={Entry}, riskAmount={Risk}, lotSize={Lot}. Returning 1.",
                    entryPrice, riskAmountPerTrade, lotSize);
                return 1;
            }

            decimal slDistancePerUnit = entryPrice - stopLoss;
            decimal slDistancePerLot  = slDistancePerUnit * lotSize;

            if (slDistancePerLot <= 0)
            {
                _logger.LogWarning(
                    "[VCPRiskManager] SL distance per lot is {Dist} (entry={Entry}, sl={SL}, lotSize={Lot}). " +
                    "Returning safe default of 1.",
                    slDistancePerLot, entryPrice, stopLoss, lotSize);
                return 1;
            }

            int lots = (int)Math.Floor(riskAmountPerTrade / slDistancePerLot);

            // Enforce 1–10 bounds
            lots = Math.Max(1, lots);
            lots = Math.Min(MaxLots, lots);

            return lots;
        }

        /// <inheritdoc/>
        public bool CanOpenTrade(int currentOpenTrades, int maxAllowedTrades)
        {
            if (maxAllowedTrades > MaxAllowedTradesCap)
            {
                _logger.LogWarning(
                    "[VCPRiskManager] maxAllowedTrades={Max} exceeds cap of {Cap}. Clamping.",
                    maxAllowedTrades, MaxAllowedTradesCap);
                maxAllowedTrades = MaxAllowedTradesCap;
            }

            maxAllowedTrades = Math.Max(1, maxAllowedTrades);

            return currentOpenTrades < maxAllowedTrades;
        }

        /// <inheritdoc/>
        public decimal CalculatePositionSize(int lots, int lotSize, decimal premium)
        {
            return Math.Round(lots * lotSize * premium, 2);
        }
    }
}
