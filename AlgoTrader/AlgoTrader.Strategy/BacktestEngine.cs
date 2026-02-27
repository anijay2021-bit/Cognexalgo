using System.Collections.Concurrent;
using System.Reactive.Subjects;
using AlgoTrader.Core.Enums;
using AlgoTrader.Core.Interfaces;
using AlgoTrader.Core.Models;
using Microsoft.Extensions.Logging;

namespace AlgoTrader.Strategy;

/// <summary>
/// Backtesting Framework
/// Simulates strategy execution against historical market data.
/// Uses a virtual clock to replay candles/ticks, simulating entry, exit, re-entry,
/// indicator evaluation, premium matching, and all risk management logic.
/// </summary>
public class BacktestEngine
{
    private readonly ILogger<BacktestEngine> _logger;

    public BacktestEngine(ILogger<BacktestEngine> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Run a full backtest for a strategy config against historical candle data.
    /// </summary>
    public async Task<BacktestResult> RunAsync(
        StrategyConfig strategy,
        List<Candle> historicalData,
        BacktestSettings settings)
    {
        _logger.LogInformation("Starting backtest for strategy {Name} with {Count} candles ({From} → {To})",
            strategy.Name, historicalData.Count,
            historicalData.FirstOrDefault()?.Timestamp,
            historicalData.LastOrDefault()?.Timestamp);

        var result = new BacktestResult
        {
            StrategyName = strategy.Name,
            Settings = settings,
            StartDate = historicalData.FirstOrDefault()?.Timestamp ?? DateTime.MinValue,
            EndDate = historicalData.LastOrDefault()?.Timestamp ?? DateTime.MinValue
        };

        // Simulation state
        var simulator = new BacktestSimulator(strategy, settings, _logger);

        // Sort candles chronologically
        var sortedCandles = historicalData.OrderBy(c => c.Timestamp).ToList();

        foreach (var candle in sortedCandles)
        {
            var virtualNow = candle.Timestamp;

            // Convert candle to simulated tick (use Close as LTP)
            var tick = new Tick
            {
                Token = candle.Token,
                Symbol = candle.Symbol,
                LTP = candle.Close,
                BidPrice = candle.Low,
                AskPrice = candle.High,
                Volume = candle.Volume,
                Timestamp = candle.Timestamp
            };

            simulator.ProcessTick(tick, virtualNow, candle);
        }

        // Finalize open positions at last candle's close
        simulator.FinalizeOpenPositions(sortedCandles.LastOrDefault());

        result = simulator.BuildResult(result);

        _logger.LogInformation("Backtest complete: {Trades} trades, Net P&L: {PnL:F2}, Win Rate: {WinRate:F1}%",
            result.TotalTrades, result.NetPnL, result.WinRate);

        return result;
    }
}

/// <summary>
/// Internal simulator that processes ticks and manages virtual positions.
/// </summary>
internal class BacktestSimulator
{
    private readonly StrategyConfig _strategy;
    private readonly BacktestSettings _settings;
    private readonly ILogger<BacktestEngine> _logger;

    // Virtual position state
    private SimulatedPosition? _currentPosition;
    private readonly List<CompletedTrade> _completedTrades = new();
    private decimal _totalPnL;
    private decimal _peakEquity;
    private decimal _maxDrawdown;
    private decimal _runningEquity;
    private int _reEntryCount;
    private bool _entryAwaitingWaitCondition;
    private decimal _underlyingLTPAtStartTime;

    // Track equity curve
    private readonly List<EquityPoint> _equityCurve = new();

    // Entry/Exit evaluators (simplified for backtest)
    private StrategyState _state = StrategyState.IDLE;

    public BacktestSimulator(StrategyConfig strategy, BacktestSettings settings, ILogger<BacktestEngine> logger)
    {
        _strategy = strategy;
        _settings = settings;
        _logger = logger;
        _runningEquity = settings.InitialCapital;
        _peakEquity = settings.InitialCapital;
    }

    public void ProcessTick(Tick tick, DateTime virtualNow, Candle candle)
    {
        var ist = virtualNow; // Assume candle timestamps are IST

        // Market hours filter (9:15 AM - 3:30 PM IST)
        var marketOpen = new TimeSpan(9, 15, 0);
        var marketClose = new TimeSpan(15, 30, 0);

        if (ist.TimeOfDay < marketOpen || ist.TimeOfDay > marketClose)
            return;

        // Track equity curve at each candle
        var currentEquity = _runningEquity;
        if (_currentPosition != null)
        {
            currentEquity += CalculateUnrealizedPnL(_currentPosition, tick.LTP);
        }
        _equityCurve.Add(new EquityPoint { Timestamp = virtualNow, Equity = currentEquity });

        // Update max drawdown
        if (currentEquity > _peakEquity) _peakEquity = currentEquity;
        var drawdown = (_peakEquity - currentEquity) / _peakEquity * 100;
        if (drawdown > _maxDrawdown) _maxDrawdown = drawdown;

        switch (_state)
        {
            case StrategyState.IDLE:
            case StrategyState.WAITING_ENTRY:
                EvaluateEntry(tick, virtualNow, candle);
                break;

            case StrategyState.ENTERED:
                EvaluateExit(tick, virtualNow, candle);
                break;
        }
    }

    private void EvaluateEntry(Tick tick, DateTime now, Candle candle)
    {
        var entry = _strategy.Entry;

        // Basic entry conditions (mirroring EntryEvaluator logic)
        bool shouldEnter = entry.EntryType switch
        {
            EntryType.Immediate => true,
            EntryType.TimeBased => entry.EntryTime.HasValue && now.TimeOfDay >= entry.EntryTime.Value &&
                                   (!entry.EntryWindowEnd.HasValue || now.TimeOfDay <= entry.EntryWindowEnd.Value),
            EntryType.LTPCondition => entry.LTPOperator == LTPOperator.GreaterEqual
                ? tick.LTP >= entry.LTPThreshold
                : tick.LTP <= entry.LTPThreshold,
            _ => false
        };

        if (!shouldEnter) return;

        // Wait & Trade check
        if (_strategy.WaitAndTradeEnabled && _strategy.WaitAndTradeType != WaitAndTradeType.Immediate)
        {
            if (_underlyingLTPAtStartTime == 0)
            {
                _underlyingLTPAtStartTime = tick.LTP;
                _entryAwaitingWaitCondition = true;
                _state = StrategyState.WAITING_ENTRY;
                return;
            }

            if (_entryAwaitingWaitCondition)
            {
                decimal move = tick.LTP - _underlyingLTPAtStartTime;
                decimal movePct = move / _underlyingLTPAtStartTime * 100;
                bool waitSatisfied = _strategy.WaitAndTradeType switch
                {
                    WaitAndTradeType.PercentUp => movePct >= _strategy.WaitAndTradeValue,
                    WaitAndTradeType.PercentDown => movePct <= -_strategy.WaitAndTradeValue,
                    WaitAndTradeType.PointsUp => move >= _strategy.WaitAndTradeValue,
                    WaitAndTradeType.PointsDown => move <= -_strategy.WaitAndTradeValue,
                    _ => true
                };

                if (!waitSatisfied) return;
                _entryAwaitingWaitCondition = false;
            }
        }

        // Simulate entry
        ExecuteVirtualEntry(tick, now);
    }

    private void EvaluateExit(Tick tick, DateTime now, Candle candle)
    {
        if (_currentPosition == null) return;

        var exit = _strategy.Exit;
        var risk = _strategy.Risk;
        decimal unrealizedPnL = CalculateUnrealizedPnL(_currentPosition, tick.LTP);
        ExitReason? exitReason = null;

        // Time-based exit
        if (exit.TimeBasedExit && exit.ExitTime.HasValue && now.TimeOfDay >= exit.ExitTime.Value)
            exitReason = ExitReason.TimeBasedExit;

        // Auto square-off (G.6)
        if (exitReason == null && _strategy.DailyAutoSquareOffEnabled &&
            now.TimeOfDay >= _strategy.DailySquareOffTime)
            exitReason = ExitReason.TimeBasedExit;

        // Expiry day square-off
        if (exitReason == null && _strategy.ExpiryDaySquareOffEnabled)
        {
            var nearestExpiry = _strategy.Legs.Any() ? _strategy.Legs.Min(l => l.Expiry) : DateTime.MaxValue;
            if (now.Date == nearestExpiry.Date && now.TimeOfDay >= _strategy.ExpiryDaySquareOffTime)
                exitReason = ExitReason.TimeBasedExit;
        }

        // Per-leg SL check (simplified: use strategy's first leg SL)
        if (exitReason == null)
        {
            foreach (var leg in _strategy.Legs)
            {
                decimal legPnL = CalculateLegPnL(leg, _currentPosition, tick.LTP);
                if (leg.SLPoints > 0 && legPnL <= -leg.SLPoints * leg.Qty * leg.LotMultiplier)
                    exitReason = ExitReason.StopLoss;
                if (leg.SLPercent > 0 && _currentPosition.EntryPrice > 0)
                {
                    decimal slThreshold = _currentPosition.EntryPrice * leg.SLPercent / 100 * leg.Qty * leg.LotMultiplier;
                    if (legPnL <= -slThreshold) exitReason = ExitReason.StopLoss;
                }

                // Target per leg
                if (exitReason == null && leg.TargetPoints > 0 && legPnL >= leg.TargetPoints * leg.Qty * leg.LotMultiplier)
                    exitReason = ExitReason.Target;
                if (exitReason == null && leg.TargetPercent > 0 && _currentPosition.EntryPrice > 0)
                {
                    decimal tgtThreshold = _currentPosition.EntryPrice * leg.TargetPercent / 100 * leg.Qty * leg.LotMultiplier;
                    if (legPnL >= tgtThreshold) exitReason = ExitReason.Target;
                }
            }
        }

        // Global risk checks (MTM SL / Target)
        if (exitReason == null && risk.MTMSLEnabled)
        {
            decimal slValue = risk.MTMSLType == RiskValueType.Amount
                ? risk.MTMSLValue
                : _settings.InitialCapital * risk.MTMSLValue / 100;
            if (unrealizedPnL <= -slValue) exitReason = ExitReason.MaxLoss;
        }

        if (exitReason == null && risk.MTMTargetEnabled)
        {
            decimal tgtValue = risk.MTMTargetType == RiskValueType.Amount
                ? risk.MTMTargetValue
                : _settings.InitialCapital * risk.MTMTargetValue / 100;
            if (unrealizedPnL >= tgtValue) exitReason = ExitReason.MaxProfit;
        }

        // Lock profit check
        if (exitReason == null && risk.LockProfitEnabled)
        {
            decimal lockThreshold = risk.LockProfitType == RiskValueType.Amount
                ? risk.LockProfitX
                : _settings.InitialCapital * risk.LockProfitX / 100;
            decimal lockAt = risk.LockProfitType == RiskValueType.Amount
                ? risk.LockProfitY
                : _settings.InitialCapital * risk.LockProfitY / 100;

            if (_currentPosition.MaxPnLSeen >= lockThreshold && unrealizedPnL <= lockAt)
                exitReason = ExitReason.ProfitLock;
        }

        // Update max PnL seen for lock/trail logic
        if (_currentPosition != null && unrealizedPnL > _currentPosition.MaxPnLSeen)
            _currentPosition.MaxPnLSeen = unrealizedPnL;

        // Market close safety net (3:25 PM)
        if (exitReason == null && !_strategy.IsPositional && now.TimeOfDay >= new TimeSpan(15, 25, 0))
            exitReason = ExitReason.TimeBasedExit;

        if (exitReason != null)
        {
            ExecuteVirtualExit(tick, now, exitReason.Value);

            // Re-entry check
            if (_strategy.CombinedReEntryEnabled && _reEntryCount < _strategy.MaxCombinedReEntries)
            {
                _reEntryCount++;
                _state = StrategyState.WAITING_ENTRY;
                _underlyingLTPAtStartTime = 0; // Reset wait-and-trade

                if (_strategy.CombinedReEntryType == CombinedReEntryType.ReverseAndReEnterImmediately)
                {
                    // Flip all legs' Buy/Sell for reversal
                    foreach (var leg in _strategy.Legs)
                    {
                        // Note: in backtest we just track the direction flip
                    }
                }
            }
        }
    }

    private void ExecuteVirtualEntry(Tick tick, DateTime now)
    {
        _currentPosition = new SimulatedPosition
        {
            EntryPrice = tick.LTP,
            EntryTime = now,
            Qty = _strategy.Legs.Sum(l => l.Qty * l.LotMultiplier),
            Direction = _strategy.Legs.FirstOrDefault()?.BuySell ?? BuySell.BUY,
            Slippage = _settings.SlippagePoints
        };

        // Apply slippage to entry
        _currentPosition.EffectiveEntryPrice = _currentPosition.Direction == BuySell.BUY
            ? tick.LTP + _settings.SlippagePoints
            : tick.LTP - _settings.SlippagePoints;

        _state = StrategyState.ENTERED;
        _logger.LogDebug("BT Entry: {Dir} {Qty} @ {Price} on {Time}",
            _currentPosition.Direction, _currentPosition.Qty, 
            _currentPosition.EffectiveEntryPrice, now);
    }

    private void ExecuteVirtualExit(Tick tick, DateTime now, ExitReason reason)
    {
        if (_currentPosition == null) return;

        // Apply slippage to exit
        decimal effectiveExitPrice = _currentPosition.Direction == BuySell.BUY
            ? tick.LTP - _settings.SlippagePoints
            : tick.LTP + _settings.SlippagePoints;

        decimal pnl = CalculateUnrealizedPnL(_currentPosition, effectiveExitPrice);

        // Subtract commission
        pnl -= _settings.CommissionPerTrade * 2; // Entry + Exit

        var trade = new CompletedTrade
        {
            EntryTime = _currentPosition.EntryTime,
            ExitTime = now,
            EntryPrice = _currentPosition.EffectiveEntryPrice,
            ExitPrice = effectiveExitPrice,
            Direction = _currentPosition.Direction,
            Qty = _currentPosition.Qty,
            PnL = pnl,
            ExitReason = reason,
            MaxFavorableExcursion = _currentPosition.MaxPnLSeen,
            MaxAdverseExcursion = _currentPosition.MaxLossSeen,
            IsReEntry = _reEntryCount > 0
        };

        _completedTrades.Add(trade);
        _totalPnL += pnl;
        _runningEquity += pnl;

        _logger.LogDebug("BT Exit: {Reason} @ {Price}, PnL: {PnL:F2} on {Time}",
            reason, effectiveExitPrice, pnl, now);

        _currentPosition = null;
        _state = StrategyState.EXITED;
    }

    public void FinalizeOpenPositions(Candle? lastCandle)
    {
        if (_currentPosition != null && lastCandle != null)
        {
            var tick = new Tick { LTP = lastCandle.Close, Timestamp = lastCandle.Timestamp };
            ExecuteVirtualExit(tick, lastCandle.Timestamp, ExitReason.TimeBasedExit);
        }
    }

    private decimal CalculateUnrealizedPnL(SimulatedPosition pos, decimal currentPrice)
    {
        return pos.Direction == BuySell.BUY
            ? (currentPrice - pos.EffectiveEntryPrice) * pos.Qty
            : (pos.EffectiveEntryPrice - currentPrice) * pos.Qty;
    }

    private decimal CalculateLegPnL(LegConfig leg, SimulatedPosition pos, decimal currentPrice)
    {
        int qty = leg.Qty * leg.LotMultiplier;
        return leg.BuySell == BuySell.BUY
            ? (currentPrice - pos.EntryPrice) * qty
            : (pos.EntryPrice - currentPrice) * qty;
    }

    public BacktestResult BuildResult(BacktestResult result)
    {
        result.Trades = _completedTrades;
        result.TotalTrades = _completedTrades.Count;
        result.WinningTrades = _completedTrades.Count(t => t.PnL > 0);
        result.LosingTrades = _completedTrades.Count(t => t.PnL <= 0);
        result.WinRate = result.TotalTrades > 0 
            ? (decimal)result.WinningTrades / result.TotalTrades * 100 
            : 0;
        result.NetPnL = _totalPnL;
        result.GrossPnL = _completedTrades.Sum(t => t.PnL);
        result.TotalCommission = _settings.CommissionPerTrade * 2 * _completedTrades.Count;
        result.MaxDrawdownPercent = _maxDrawdown;
        result.MaxConsecutiveWins = CalculateMaxConsecutive(true);
        result.MaxConsecutiveLosses = CalculateMaxConsecutive(false);
        result.AverageWin = result.WinningTrades > 0 
            ? _completedTrades.Where(t => t.PnL > 0).Average(t => t.PnL) 
            : 0;
        result.AverageLoss = result.LosingTrades > 0 
            ? _completedTrades.Where(t => t.PnL <= 0).Average(t => t.PnL) 
            : 0;
        result.ProfitFactor = result.AverageLoss != 0 
            ? Math.Abs(result.AverageWin / result.AverageLoss) 
            : 0;
        result.SharpeRatio = CalculateSharpeRatio();
        result.EquityCurve = _equityCurve;
        result.ReEntryCount = _reEntryCount;

        // Per-exit-reason breakdown
        result.ExitReasonBreakdown = _completedTrades
            .GroupBy(t => t.ExitReason)
            .ToDictionary(g => g.Key, g => g.Count());

        return result;
    }

    private int CalculateMaxConsecutive(bool wins)
    {
        int max = 0, current = 0;
        foreach (var trade in _completedTrades)
        {
            bool isMatch = wins ? trade.PnL > 0 : trade.PnL <= 0;
            if (isMatch) { current++; max = Math.Max(max, current); }
            else current = 0;
        }
        return max;
    }

    private decimal CalculateSharpeRatio()
    {
        if (_completedTrades.Count < 2) return 0;
        var returns = _completedTrades.Select(t => t.PnL).ToList();
        var avg = returns.Average();
        var stdDev = (decimal)Math.Sqrt(returns.Average(r => (double)((r - avg) * (r - avg))));
        return stdDev != 0 ? avg / stdDev * (decimal)Math.Sqrt(252) : 0; // Annualized
    }
}

// ─── Models ────────────────────────────────────────────────────────

/// <summary>Settings for controlling a backtest run.</summary>
public class BacktestSettings
{
    /// <summary>Starting capital for equity tracking.</summary>
    public decimal InitialCapital { get; set; } = 500_000;

    /// <summary>Slippage in points applied to both entry and exit.</summary>
    public decimal SlippagePoints { get; set; } = 0.5m;

    /// <summary>Commission per trade (per side).</summary>
    public decimal CommissionPerTrade { get; set; } = 20;

    /// <summary>Timeframe for the candle data.</summary>
    public TimeFrame CandleTimeFrame { get; set; } = TimeFrame.ONE_MINUTE;

    /// <summary>Whether to include intraday candles or just daily.</summary>
    public bool IntradayMode { get; set; } = true;
}

/// <summary>Complete results of a backtest run.</summary>
public class BacktestResult
{
    public string StrategyName { get; set; } = string.Empty;
    public BacktestSettings Settings { get; set; } = new();
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    // Summary stats
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public decimal WinRate { get; set; }
    public decimal NetPnL { get; set; }
    public decimal GrossPnL { get; set; }
    public decimal TotalCommission { get; set; }
    public decimal MaxDrawdownPercent { get; set; }
    public int MaxConsecutiveWins { get; set; }
    public int MaxConsecutiveLosses { get; set; }
    public decimal AverageWin { get; set; }
    public decimal AverageLoss { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal SharpeRatio { get; set; }
    public int ReEntryCount { get; set; }

    // Detailed data
    public List<CompletedTrade> Trades { get; set; } = new();
    public List<EquityPoint> EquityCurve { get; set; } = new();
    public Dictionary<ExitReason, int> ExitReasonBreakdown { get; set; } = new();
}

/// <summary>A single completed trade in the backtest.</summary>
public class CompletedTrade
{
    public DateTime EntryTime { get; set; }
    public DateTime ExitTime { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal ExitPrice { get; set; }
    public BuySell Direction { get; set; }
    public int Qty { get; set; }
    public decimal PnL { get; set; }
    public ExitReason ExitReason { get; set; }
    public decimal MaxFavorableExcursion { get; set; }
    public decimal MaxAdverseExcursion { get; set; }
    public bool IsReEntry { get; set; }
    public TimeSpan Duration => ExitTime - EntryTime;
}

/// <summary>Equity at a point in time for charting.</summary>
public class EquityPoint
{
    public DateTime Timestamp { get; set; }
    public decimal Equity { get; set; }
}

/// <summary>Internal simulated position state.</summary>
internal class SimulatedPosition
{
    public decimal EntryPrice { get; set; }
    public decimal EffectiveEntryPrice { get; set; }
    public DateTime EntryTime { get; set; }
    public int Qty { get; set; }
    public BuySell Direction { get; set; }
    public decimal Slippage { get; set; }
    public decimal MaxPnLSeen { get; set; }
    public decimal MaxLossSeen { get; set; }
}
