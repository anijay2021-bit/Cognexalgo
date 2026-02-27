using System.Reactive.Linq;
using System.Reactive.Subjects;
using AlgoTrader.Core.Enums;
using AlgoTrader.Core.Interfaces;
using AlgoTrader.Core.Models;
using Microsoft.Extensions.Logging;

namespace AlgoTrader.RMS;

/// <summary>Risk Manager — monitors MTM streams and enforces risk limits.</summary>
public class RiskManager : IRiskManager
{
    private readonly ILogger<RiskManager> _logger;
    private readonly Subject<RiskEvent> _riskAlerts = new();
    private readonly Dictionary<string, int> _maxLossTickCounter = new();
    private readonly Dictionary<string, int> _maxProfitTickCounter = new();
    private CancellationTokenSource? _cts;
    private IDisposable? _subscription;

    public IObservable<RiskEvent> RiskAlerts => _riskAlerts.AsObservable();

    public GlobalRiskConfig Config { get; set; } = new();

    public RiskManager(ILogger<RiskManager> logger)
    {
        _logger = logger;
    }

    public Task StartAsync()
    {
        _cts = new CancellationTokenSource();
        _logger.LogInformation("RiskManager started");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        _subscription?.Dispose();
        _logger.LogInformation("RiskManager stopped");
        return Task.CompletedTask;
    }

    /// <summary>Subscribe to MTM updates and evaluate risk rules.</summary>
    public void MonitorMTM(IObservable<PositionSummary> mtmStream)
    {
        _subscription = mtmStream.Subscribe(summary =>
        {
            EvaluateRiskRules(summary);
        });
    }

    private void EvaluateRiskRules(PositionSummary summary)
    {
        var stratId = summary.StrategyId;

        // MaxLoss check
        if (Config.GlobalMaxLoss > 0 && summary.TotalMTM <= -Config.GlobalMaxLoss)
        {
            var count = _maxLossTickCounter.GetValueOrDefault(stratId) + 1;
            _maxLossTickCounter[stratId] = count;
            if (count >= 3) // 3 consecutive tick breach
            {
                _riskAlerts.OnNext(new RiskEvent(RiskEventType.MaxLoss, stratId, "", Config.GlobalMaxLoss, Math.Abs(summary.TotalMTM), DateTime.UtcNow));
                _logger.LogWarning("RMS: MaxLoss triggered for {StrategyId} — MTM: {MTM}", stratId, summary.TotalMTM);
            }
        }
        else
        {
            _maxLossTickCounter.Remove(stratId);
        }

        // MaxProfit check
        if (Config.GlobalMaxProfit > 0 && summary.TotalMTM >= Config.GlobalMaxProfit)
        {
            var count = _maxProfitTickCounter.GetValueOrDefault(stratId) + 1;
            _maxProfitTickCounter[stratId] = count;
            if (count >= 3)
            {
                _riskAlerts.OnNext(new RiskEvent(RiskEventType.MaxProfit, stratId, "", Config.GlobalMaxProfit, summary.TotalMTM, DateTime.UtcNow));
                _logger.LogInformation("RMS: MaxProfit triggered for {StrategyId} — MTM: {MTM}", stratId, summary.TotalMTM);
            }
        }
        else
        {
            _maxProfitTickCounter.Remove(stratId);
        }
    }
}

/// <summary>Time-based exit service.</summary>
public class TimeBasedExitService : IDisposable
{
    private readonly ILogger<TimeBasedExitService> _logger;
    private readonly Subject<RiskEvent> _exitEvents = new();
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;

    public IObservable<RiskEvent> ExitEvents => _exitEvents.AsObservable();
    public TimeSpan? GlobalExitTime { get; set; }

    public TimeBasedExitService(ILogger<TimeBasedExitService> logger) => _logger = logger;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        _ = Task.Run(async () =>
        {
            while (await _timer.WaitForNextTickAsync(_cts.Token))
            {
                var ist = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                    TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"));

                if (GlobalExitTime.HasValue && ist.TimeOfDay >= GlobalExitTime.Value)
                {
                    _exitEvents.OnNext(new RiskEvent(RiskEventType.TimeBased, "ALL", "ALL", 0, 0, DateTime.UtcNow));
                    _logger.LogInformation("RMS: Time-based exit triggered at {Time}", ist.TimeOfDay);
                }
            }
        });
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _timer?.Dispose();
    }
}

/// <summary>Global risk configuration.</summary>
public class GlobalRiskConfig
{
    public decimal GlobalMaxLoss { get; set; }
    public decimal GlobalMaxProfit { get; set; }
    public bool IsPositionalSession { get; set; }
    public TimeSpan? GlobalExitTime { get; set; }
    public int CheckIntervalMs { get; set; } = 1000;
}
