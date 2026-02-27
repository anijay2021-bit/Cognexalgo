using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AlgoTrader.Core.Models;
using AlgoTrader.Core.Enums;
using AlgoTrader.Core.Interfaces;
using AlgoTrader.Data.Repositories;
using AlgoTrader.MarketData;

namespace AlgoTrader.RMS;

public class AutoSquareOffService : BackgroundService
{
    private readonly IStrategyEngine _engine;
    private readonly StrategyRepository _repo;
    private readonly ExpiryResolver _expiryResolver;
    private readonly ILogger<AutoSquareOffService> _logger;
    
    public AutoSquareOffService(
        IStrategyEngine engine,
        StrategyRepository repo,
        ExpiryResolver expiryResolver,
        ILogger<AutoSquareOffService> logger)
    {
        _engine = engine;
        _repo = repo;
        _expiryResolver = expiryResolver;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.Now;
                var strategies = _repo.FindAll().Where(s => s.IsActive && s.State == StrategyState.ENTERED).ToList();
                
                foreach (var strategy in strategies)
                {
                    // Daily auto square-off
                    if (strategy.DailyAutoSquareOffEnabled &&
                        now.TimeOfDay >= strategy.DailySquareOffTime &&
                        now.TimeOfDay < strategy.DailySquareOffTime.Add(TimeSpan.FromMinutes(1)))
                    {
                        _logger.LogInformation("Daily auto square-off: {s}", strategy.Name);
                        await _engine.ExitStrategyAsync(strategy.Id, ExitReason.TimeBasedExit);
                    }
                    
                    // Expiry day square-off
                    if (strategy.ExpiryDaySquareOffEnabled && strategy.Legs.Any())
                    {
                        var expiry = await _expiryResolver.ResolveExpiryAsync(
                            strategy.Legs[0].UnderlyingSymbol, Exchange.NFO, 
                            ExpirySelectionType.Nearest);
                        
                        if (_expiryResolver.IsExpiryDay(expiry) &&
                            now.TimeOfDay >= strategy.ExpiryDaySquareOffTime)
                        {
                            _logger.LogInformation("Expiry day auto square-off: {s}", strategy.Name);
                            await _engine.ExitStrategyAsync(strategy.Id, ExitReason.TimeBasedExit);
                        }
                    }
                    
                    // N days before expiry exit
                    if (strategy.PositionalDurationType == PositionalDurationType.NBeforeExpiry && strategy.Legs.Any())
                    {
                        var expiry = await _expiryResolver.ResolveExpiryAsync(
                            strategy.Legs[0].UnderlyingSymbol, Exchange.NFO,
                            ExpirySelectionType.Nearest);
                        if (_expiryResolver.DaysToExpiry(expiry) <= strategy.DaysBeforeExpiry)
                        {
                            _logger.LogInformation("N-Days before expiry auto square-off: {s}", strategy.Name);
                            await _engine.ExitStrategyAsync(strategy.Id, ExitReason.TimeBasedExit);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AutoSquareOffService");
            }
            
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }
}
