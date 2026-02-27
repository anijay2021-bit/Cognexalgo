using AlgoTrader.Core.Enums;
using AlgoTrader.Core.Interfaces;
using AlgoTrader.Core.Models;
using Microsoft.Extensions.Logging;

namespace AlgoTrader.Brokers;

/// <summary>Factory to create IBroker instances based on BrokerType.</summary>
public class BrokerFactory : IBrokerFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<BrokerFactory> _logger;

    public BrokerFactory(IServiceProvider serviceProvider, ILoggerFactory loggerFactory, ILogger<BrokerFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public IBroker Create(BrokerType brokerType)
    {
        _logger.LogDebug("Creating broker adapter for {BrokerType}", brokerType);
        return brokerType switch
        {
            BrokerType.AngelOne => new AngelOne.AngelOneBroker(_loggerFactory.CreateLogger<AngelOne.AngelOneBroker>()),
            // Future: BrokerType.Zerodha => new Zerodha.ZerodhaBroker(_loggerFactory.CreateLogger<Zerodha.ZerodhaBroker>()),
            _ => throw new NotSupportedException($"Broker type {brokerType} not yet supported")
        };
    }

    public IBroker CreateFromCredential(AccountCredential credential)
        => Create(credential.BrokerType);
}
