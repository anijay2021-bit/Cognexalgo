using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AlgoTrader.Core.Interfaces;
using AlgoTrader.Core.Models;
using AlgoTrader.Core.Enums;
using Microsoft.Extensions.Logging;

namespace AlgoTrader.OMS;

public class HoldingsService
{
    private readonly IBrokerFactory _brokerFactory;
    private readonly ILogger<HoldingsService> _logger;
    private readonly List<Position> _holdingsCache = new();
    
    public HoldingsService(IBrokerFactory brokerFactory, ILogger<HoldingsService> logger)
    {
        _brokerFactory = brokerFactory;
        _logger = logger;
    }

    public async Task<List<Position>> GetHoldingsAsync(AccountCredential account, bool forceRefresh = false)
    {
        if (!forceRefresh && _holdingsCache.Count > 0)
        {
            return _holdingsCache;
        }

        try
        {
            var broker = _brokerFactory.Create(account.BrokerType);
            var positions = await broker.GetPositionBookAsync(account.JWTToken);
            
            _holdingsCache.Clear();
            _holdingsCache.AddRange(positions);
            
            return _holdingsCache;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch holdings for account {Id}", account.ClientID);
            return new List<Position>();
        }
    }

    public decimal CalculateTotalUnrealizedPnL()
    {
        decimal total = 0;
        foreach (var pos in _holdingsCache)
        {
            total += pos.MTM - pos.RealizedPnL;
        }
        return total;
    }
}
