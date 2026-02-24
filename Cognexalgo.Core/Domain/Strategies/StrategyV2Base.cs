using System;
using System.Threading;
using System.Threading.Tasks;
using Cognexalgo.Core.Domain.Enums;
using Cognexalgo.Core.Domain.Entities;

namespace Cognexalgo.Core.Domain.Strategies
{
    /// <summary>
    /// Base class for all V2 strategies. Provides lifecycle, isolation, and state management.
    /// Each strategy runs in its own Task with its own CancellationTokenSource (Module 9).
    /// </summary>
    public abstract class StrategyV2Base
    {
        // ─── Identity ────────────────────────────────────────────
        public string StrategyId { get; protected set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public StrategyType Type { get; protected set; }
        public StrategyStatus Status { get; set; } = StrategyStatus.Draft;
        public TradingMode TradingMode { get; set; } = TradingMode.PaperTrade;

        // ─── Isolation (Module 9) ────────────────────────────────
        public CancellationTokenSource Cts { get; private set; } = new();
        public int ConsecutiveErrors { get; set; } = 0;
        public const int CircuitBreakerThreshold = 3;
        public bool IsCircuitBroken => ConsecutiveErrors >= CircuitBreakerThreshold;

        // ─── State Machine ───────────────────────────────────────
        public SignalState CurrentState { get; set; } = SignalState.WAITING;

        // ─── Callbacks ───────────────────────────────────────────
        public event Action<string, string>? OnLog;       // (level, message)
        public event Action<Signal>? OnSignalFired;
        public event Action<Order>? OnOrderPlaced;
        public event Action<Exception>? OnError;

        // ─── Lifecycle ───────────────────────────────────────────

        /// <summary>Initialize strategy: load history, warm up indicators.</summary>
        public virtual Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;

        /// <summary>Called on every tick. Core evaluation logic.</summary>
        public abstract Task OnTickAsync(TickContext tick, CancellationToken ct);

        /// <summary>Called when a candle closes for the strategy's timeframe.</summary>
        public virtual Task OnCandleClosedAsync(CandleContext candle, CancellationToken ct) 
            => Task.CompletedTask;

        /// <summary>Reset the strategy for re-entry after exit.</summary>
        public virtual void ResetForReEntry()
        {
            CurrentState = SignalState.WAITING;
            ConsecutiveErrors = 0;
        }

        /// <summary>Stop the strategy and cancel its task.</summary>
        public virtual void Stop()
        {
            Status = StrategyStatus.Paused;
            Cts.Cancel();
            Cts = new CancellationTokenSource(); // Fresh token for restart
        }

        /// <summary>Record error and check circuit breaker.</summary>
        public void RecordError(Exception ex)
        {
            ConsecutiveErrors++;
            OnError?.Invoke(ex);
            Log("ERROR", $"[{Name}] Error #{ConsecutiveErrors}: {ex.Message}");

            if (IsCircuitBroken)
            {
                Status = StrategyStatus.Error;
                Log("ERROR", $"[{Name}] CIRCUIT BREAKER TRIPPED: {CircuitBreakerThreshold} consecutive errors. Strategy auto-paused.");
            }
        }

        /// <summary>Record successful execution, resetting error counter.</summary>
        public void RecordSuccess()
        {
            ConsecutiveErrors = 0;
        }

        protected void Log(string level, string message)
        {
            OnLog?.Invoke(level, message);
        }

        /// <summary>Invoke from derived classes to fire signal events.</summary>
        protected void FireSignal(Signal signal) => FireSignal(signal);

        /// <summary>Invoke from derived classes to fire order placed events.</summary>
        protected void FireOrderPlaced(Order order) => OnOrderPlaced?.Invoke(order);

        /// <summary>Invoke from derived classes to fire error events.</summary>
        protected void FireError(Exception ex) => OnError?.Invoke(ex);
    }

    // ─── Signal State Machine (Module 3) ─────────────────────────
    public enum SignalState
    {
        WAITING,
        ENTRY_TRIGGERED,
        IN_POSITION,
        EXIT_TRIGGERED,
        COMPLETED
    }

    // ─── Tick/Candle context passed to strategy ──────────────────
    public class TickContext
    {
        public string Symbol { get; set; } = string.Empty;
        public decimal Ltp { get; set; }
        public decimal BidPrice { get; set; }
        public decimal AskPrice { get; set; }
        public long Volume { get; set; }
        public long OpenInterest { get; set; }
        public DateTime Timestamp { get; set; }

        // Multi-index LTPs
        public decimal NiftyLtp { get; set; }
        public decimal BankNiftyLtp { get; set; }
        public decimal FinniftyLtp { get; set; }
    }

    public class CandleContext
    {
        public string Symbol { get; set; } = string.Empty;
        public TimeFrame TimeFrame { get; set; }
        public DateTime Timestamp { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public long Volume { get; set; }
    }
}
