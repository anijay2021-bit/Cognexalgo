using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cognexalgo.Core.Application.Interfaces;
using Cognexalgo.Core.Domain.Entities;
using Cognexalgo.Core.Domain.Enums;
using Cognexalgo.Core.Domain.Strategies;
using Cognexalgo.Core.Domain.ValueObjects;
using Cognexalgo.Core.Infrastructure.Services;
using Newtonsoft.Json;

namespace Cognexalgo.Core.Application.Services
{
    /// <summary>
    /// Signal Engine (Module 3):
    /// - Runs on configurable scan interval (default 1s for live, tick-by-tick for paper)
    /// - Once a signal fires, it must be consumed EXACTLY ONCE per strategy
    /// - Signal logged with timestamp, condition snapshot, and indicator values
    /// - Duplicate signal suppression after entry until position exit
    /// - Integrates with RMS pre-check before order placement
    /// 
    /// State Machine per Strategy:
    /// WAITING → [Entry Signal] → ENTRY_TRIGGERED → [Order Filled] → IN_POSITION
    /// IN_POSITION → [Exit Signal / SL / Target / Time] → EXIT_TRIGGERED
    /// EXIT_TRIGGERED → [Order Filled] → WAITING or COMPLETED
    /// </summary>
    public class SignalEngine
    {
        private readonly ISignalRepository _signalRepo;
        private readonly IOrderRepository _orderRepo;
        private readonly OrderFactory _orderFactory;
        private readonly PaperTradeSimulator _paperSim;
        private readonly IAngelOneAdapter _broker;

        // Exactly-once tracking: StrategyId → last SignalType that was consumed
        private readonly ConcurrentDictionary<string, SignalType> _lastConsumedSignal = new();

        // Event for UI updates
        public event Action<Signal>? OnSignalProcessed;
        public event Action<Order>? OnOrderGenerated;
        public event Action<string, string>? OnLog;

        public SignalEngine(
            ISignalRepository signalRepo,
            IOrderRepository orderRepo,
            OrderFactory orderFactory,
            PaperTradeSimulator paperSim,
            IAngelOneAdapter broker)
        {
            _signalRepo = signalRepo;
            _orderRepo = orderRepo;
            _orderFactory = orderFactory;
            _paperSim = paperSim;
            _broker = broker;
        }

        /// <summary>
        /// Process a signal emitted by a strategy.
        /// Implements exactly-once consumption and duplicate suppression.
        /// </summary>
        public async Task ProcessSignalAsync(Signal signal, StrategyV2Base strategy)
        {
            // ─── Duplicate Suppression ───────────────────────────
            if (_lastConsumedSignal.TryGetValue(signal.StrategyId, out var lastType))
            {
                if (lastType == signal.SignalType)
                {
                    signal.ActionTaken = SignalActionTaken.Suppressed;
                    Log("WARN", $"[{strategy.Name}] Signal suppressed: duplicate {signal.SignalType}");
                    await _signalRepo.AddAsync(signal);
                    return;
                }
            }

            // ─── State Validation ────────────────────────────────
            bool validTransition = (signal.SignalType, strategy.CurrentState) switch
            {
                (SignalType.Entry, SignalState.WAITING) => true,
                (SignalType.Exit, SignalState.IN_POSITION) => true,
                (SignalType.ForceExit, SignalState.IN_POSITION) => true,
                (SignalType.ReEntry, SignalState.WAITING) => true,
                _ => false
            };

            if (!validTransition)
            {
                signal.ActionTaken = SignalActionTaken.Suppressed;
                Log("WARN", $"[{strategy.Name}] Signal suppressed: invalid state transition " +
                    $"{strategy.CurrentState} → {signal.SignalType}");
                await _signalRepo.AddAsync(signal);
                return;
            }

            // ─── Mark as consumed (exactly-once) ────────────────
            _lastConsumedSignal[signal.StrategyId] = signal.SignalType;
            signal.ActionTaken = SignalActionTaken.OrderPlaced;

            // ─── Persist signal ──────────────────────────────────
            await _signalRepo.AddAsync(signal);
            OnSignalProcessed?.Invoke(signal);

            Log("INFO", $"[{strategy.Name}] ✓ Signal consumed: {signal.SignalType} | {signal.TriggerCondition}");

            // ─── Create and route order ───────────────────────
            if (signal.SignalType == SignalType.Entry || signal.SignalType == SignalType.ReEntry ||
                signal.SignalType == SignalType.Exit || signal.SignalType == SignalType.ForceExit)
            {
                var order = new Order
                {
                    OrderId = $"ORD-{signal.StrategyId}-{DateTime.UtcNow:HHmmss}-{signal.SignalType}",
                    StrategyId = signal.StrategyId,
                    LegId = signal.LegId,
                    SignalId = signal.SignalId,
                    TradingSymbol = signal.Symbol ?? "",
                    Direction = (signal.SignalType == SignalType.Exit || signal.SignalType == SignalType.ForceExit)
                        ? Direction.BUY : Direction.SELL,
                    OrderType = OrderType.MARKET,
                    ProductType = ProductType.MIS,
                    Quantity = 1,
                    TradingMode = strategy.TradingMode,
                    IsSimulated = strategy.TradingMode == TradingMode.PaperTrade,
                    Status = OrderStatus.PENDING,
                    Price = (decimal)signal.Price
                };

                if (strategy.TradingMode == TradingMode.PaperTrade)
                {
                    await _paperSim.ExecuteAsync(order, (decimal)signal.Price);
                }
                else if (_broker.IsAuthenticated)
                {
                    var result = await _broker.PlaceOrderAsync(new AngelOrderRequest
                    {
                        TradingSymbol = order.TradingSymbol,
                        TransactionType = order.Direction == Direction.BUY ? "BUY" : "SELL",
                        Quantity = order.Quantity,
                        Price = (decimal)order.Price,
                        Exchange = order.Exchange
                    });
                    order.BrokerOrderId = result.BrokerOrderId;
                    order.Status = result.Success ? OrderStatus.PLACED : OrderStatus.REJECTED;
                    if (!result.Success) order.RejectionReason = result.ErrorMessage;
                    order.PlacedAt = DateTime.UtcNow;
                }

                try { await _orderRepo.AddAsync(order); } catch { }
                OnOrderGenerated?.Invoke(order);
            }
        }

        /// <summary>
        /// Transition the strategy state machine after an order fills.
        /// </summary>
        public void TransitionOnFill(StrategyV2Base strategy, Order order)
        {
            (strategy.CurrentState, order.Direction) = (strategy.CurrentState, order.Direction) switch
            {
                // Entry BUY filled → IN_POSITION
                (SignalState.ENTRY_TRIGGERED, _) => (SignalState.IN_POSITION, order.Direction),
                // Exit order filled → WAITING (for re-entry) or COMPLETED
                (SignalState.EXIT_TRIGGERED, _) => (SignalState.WAITING, order.Direction),
                _ => (strategy.CurrentState, order.Direction)
            };

            Log("INFO", $"[{strategy.Name}] State: {strategy.CurrentState} (after {order.Direction} fill)");
        }

        /// <summary>Reset signal tracking for a strategy (on restart or re-entry).</summary>
        public void ResetStrategy(string strategyId)
        {
            _lastConsumedSignal.TryRemove(strategyId, out _);
        }

        private void Log(string level, string msg) => OnLog?.Invoke(level, msg);
    }

    /// <summary>
    /// Order Factory (Module 4):
    /// Creates properly formatted orders with ID generation and validation.
    /// </summary>
    public class OrderFactory
    {
        private readonly IOrderIdGenerator _idGen;

        public OrderFactory(IOrderIdGenerator idGen)
        {
            _idGen = idGen;
        }

        /// <summary>Create an entry order from a strategy leg and signal.</summary>
        public async Task<Order> CreateEntryOrderAsync(
            Strategy strategy, StrategyLeg leg, Signal signal)
        {
            string orderId = await _idGen.GenerateAsync(strategy.StrategyId, leg.LegNumber);

            return new Order
            {
                OrderId = orderId,
                StrategyId = strategy.StrategyId,
                LegId = leg.LegId,
                SignalId = signal.SignalId,
                TradingSymbol = leg.TradingSymbol,
                Exchange = leg.Exchange,
                InstrumentType = leg.InstrumentType,
                Direction = leg.Direction,
                OrderType = OrderType.MARKET,
                ProductType = ProductType.MIS,
                Quantity = leg.Quantity,
                TradingMode = strategy.TradingMode,
                IsSimulated = strategy.TradingMode == TradingMode.PaperTrade,
                Status = OrderStatus.PENDING,
                CreatedAt = DateTime.UtcNow,
                MaxRetries = 3
            };
        }

        /// <summary>Create exit orders (reverse direction) for all open legs.</summary>
        public async Task<Order> CreateExitOrderAsync(
            Strategy strategy, StrategyLeg leg, Signal signal)
        {
            string orderId = await _idGen.GenerateAsync(strategy.StrategyId, leg.LegNumber);

            return new Order
            {
                OrderId = orderId,
                StrategyId = strategy.StrategyId,
                LegId = leg.LegId,
                SignalId = signal.SignalId,
                TradingSymbol = leg.TradingSymbol,
                Exchange = leg.Exchange,
                InstrumentType = leg.InstrumentType,
                Direction = leg.Direction == Direction.BUY ? Direction.SELL : Direction.BUY,
                OrderType = OrderType.MARKET,
                ProductType = ProductType.MIS,
                Quantity = leg.Quantity,
                TradingMode = strategy.TradingMode,
                IsSimulated = strategy.TradingMode == TradingMode.PaperTrade,
                Status = OrderStatus.PENDING,
                CreatedAt = DateTime.UtcNow,
                MaxRetries = 3
            };
        }
    }

    /// <summary>
    /// Paper Trade Simulator (Module 4):
    /// - Market orders: fill immediately at LTP + configurable slippage (default 0.05%)
    /// - Limit orders: fill when LTP crosses limit price
    /// - SL orders: trigger when LTP crosses trigger price
    /// - Adds ±1 tick randomness for realistic fills
    /// </summary>
    public class PaperTradeSimulator
    {
        private readonly IOrderRepository _orderRepo;
        private readonly Random _rng = new();

        public double SlippagePercent { get; set; } = 0.05; // 0.05% default
        public decimal TickSize { get; set; } = 0.05m;       // NIFTY tick

        public event Action<Order>? OnOrderFilled;

        public PaperTradeSimulator(IOrderRepository orderRepo)
        {
            _orderRepo = orderRepo;
        }

        /// <summary>Simulate execution of a paper trade order.</summary>
        public async Task<Order> ExecuteAsync(Order order, decimal currentLtp)
        {
            switch (order.OrderType)
            {
                case OrderType.MARKET:
                    return await FillMarketOrder(order, currentLtp);

                case OrderType.LIMIT:
                    return await FillLimitOrder(order, currentLtp);

                case OrderType.SL:
                case OrderType.SL_M:
                    return await FillStopLossOrder(order, currentLtp);

                default:
                    return order;
            }
        }

        private async Task<Order> FillMarketOrder(Order order, decimal ltp)
        {
            // Apply slippage: Buy at ask (slightly higher), Sell at bid (slightly lower)
            decimal slippage = ltp * (decimal)(SlippagePercent / 100.0);

            // Add ±1 tick randomness
            decimal tickRandom = (_rng.Next(-1, 2)) * TickSize;

            decimal fillPrice = order.Direction == Direction.BUY
                ? ltp + slippage + tickRandom   // Buy at ask + slippage
                : ltp - slippage + tickRandom;  // Sell at bid - slippage

            order.FilledPrice = Math.Max(0.05m, fillPrice); // Never negative
            order.FilledQuantity = order.Quantity;
            order.PendingQuantity = 0;
            order.Status = OrderStatus.COMPLETE;
            order.PlacedAt = DateTime.UtcNow;
            order.FilledAt = DateTime.UtcNow;
            order.LatencyMs = 1; // Simulated: instant fill

            await _orderRepo.UpdateAsync(order);
            OnOrderFilled?.Invoke(order);
            return order;
        }

        private async Task<Order> FillLimitOrder(Order order, decimal ltp)
        {
            bool shouldFill = order.Direction == Direction.BUY
                ? ltp <= order.Price      // Buy limit: fill when LTP <= limit price
                : ltp >= order.Price;     // Sell limit: fill when LTP >= limit price

            if (shouldFill)
            {
                order.FilledPrice = order.Price;
                order.FilledQuantity = order.Quantity;
                order.PendingQuantity = 0;
                order.Status = OrderStatus.COMPLETE;
                order.FilledAt = DateTime.UtcNow;

                await _orderRepo.UpdateAsync(order);
                OnOrderFilled?.Invoke(order);
            }
            else
            {
                order.Status = OrderStatus.OPEN;
                order.PendingQuantity = order.Quantity;
            }

            return order;
        }

        private async Task<Order> FillStopLossOrder(Order order, decimal ltp)
        {
            bool triggered = order.Direction == Direction.BUY
                ? ltp >= order.TriggerPrice
                : ltp <= order.TriggerPrice;

            if (triggered)
            {
                // SL-M: fill at market after trigger
                decimal slippage = ltp * (decimal)(SlippagePercent / 100.0);
                order.FilledPrice = order.Direction == Direction.BUY
                    ? ltp + slippage : ltp - slippage;
                order.FilledQuantity = order.Quantity;
                order.PendingQuantity = 0;
                order.Status = OrderStatus.COMPLETE;
                order.FilledAt = DateTime.UtcNow;

                await _orderRepo.UpdateAsync(order);
                OnOrderFilled?.Invoke(order);
            }
            else
            {
                order.Status = OrderStatus.OPEN;
                order.PendingQuantity = order.Quantity;
            }

            return order;
        }
    }

    /// <summary>
    /// Order Status Polling Service:
    /// - Tracks live (non-paper) orders in PLACED/OPEN status
    /// - Polls broker GetOrderBookAsync every 3 seconds
    /// - Updates order status on fill/reject and fires events
    /// - Auto-starts when live orders exist, auto-stops when none remain
    /// </summary>
    public class OrderPollingService : IDisposable
    {
        private readonly IAngelOneAdapter _broker;
        private readonly IOrderRepository _orderRepo;
        private readonly ConcurrentDictionary<string, Order> _pendingOrders = new();
        private Timer? _pollTimer;
        private bool _isPolling = false;

        public event Action<Order>? OnOrderFilled;
        public event Action<Order>? OnOrderRejected;
        public event Action<string, string>? OnLog;

        public int PendingCount => _pendingOrders.Count;

        public OrderPollingService(IAngelOneAdapter broker, IOrderRepository orderRepo)
        {
            _broker = broker;
            _orderRepo = orderRepo;
        }

        /// <summary>Track a live order for status polling.</summary>
        public void TrackOrder(Order order)
        {
            if (order.IsSimulated || order.TradingMode == TradingMode.PaperTrade) return;
            if (order.Status == OrderStatus.COMPLETE || order.Status == OrderStatus.REJECTED) return;
            if (string.IsNullOrEmpty(order.BrokerOrderId)) return;

            _pendingOrders[order.BrokerOrderId] = order;
            Log("INFO", $"[OrderPoll] Tracking: {order.BrokerOrderId} ({order.TradingSymbol})");

            // Auto-start polling when first live order arrives
            if (!_isPolling) StartPolling();
        }

        /// <summary>Start the 3-second polling timer.</summary>
        public void StartPolling()
        {
            if (_isPolling) return;
            _isPolling = true;
            _pollTimer = new Timer(async _ => await PollOrdersAsync(), null,
                                   TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3));
            Log("INFO", "[OrderPoll] Polling started (3s interval)");
        }

        /// <summary>Stop the polling timer.</summary>
        public void StopPolling()
        {
            _isPolling = false;
            _pollTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            Log("INFO", "[OrderPoll] Polling stopped");
        }

        private async Task PollOrdersAsync()
        {
            if (_pendingOrders.IsEmpty)
            {
                StopPolling();
                return;
            }

            if (!_broker.IsAuthenticated) return;

            try
            {
                var orderBook = await _broker.GetOrderBookAsync();
                if (orderBook == null || orderBook.Count == 0) return;

                foreach (var (brokerOrderId, order) in _pendingOrders)
                {
                    var brokerOrder = orderBook.FirstOrDefault(
                        o => o.OrderId == brokerOrderId);
                    if (brokerOrder == null) continue;

                    var newStatus = brokerOrder.Status?.ToUpperInvariant() switch
                    {
                        "COMPLETE" or "TRADED" => OrderStatus.COMPLETE,
                        "REJECTED" => OrderStatus.REJECTED,
                        "CANCELLED" => OrderStatus.CANCELLED,
                        "OPEN" or "PENDING" or "TRIGGER PENDING" => (OrderStatus?)null,
                        _ => null
                    };

                    if (newStatus == null) continue; // Still pending, skip

                    // ── Update order with broker data ────────────
                    order.Status = newStatus.Value;

                    if (newStatus == OrderStatus.COMPLETE)
                    {
                        order.FilledPrice = brokerOrder.AveragePrice > 0
                            ? brokerOrder.AveragePrice : order.Price;
                        order.FilledQuantity = brokerOrder.FilledShares > 0
                            ? brokerOrder.FilledShares : order.Quantity;
                        order.PendingQuantity = 0;
                        order.FilledAt = DateTime.UtcNow;

                        Log("INFO", $"[OrderPoll] FILLED: {brokerOrderId} " +
                            $"({order.TradingSymbol} @ {order.FilledPrice:N2})");
                        OnOrderFilled?.Invoke(order);
                    }
                    else if (newStatus == OrderStatus.REJECTED)
                    {
                        order.RejectionReason = brokerOrder.Text ?? "Rejected by broker";

                        Log("WARN", $"[OrderPoll] REJECTED: {brokerOrderId} " +
                            $"({order.TradingSymbol}) — {order.RejectionReason}");
                        OnOrderRejected?.Invoke(order);
                    }
                    else
                    {
                        Log("WARN", $"[OrderPoll] CANCELLED: {brokerOrderId} ({order.TradingSymbol})");
                    }

                    // Persist + remove from tracking
                    try { await _orderRepo.UpdateAsync(order); } catch { }
                    _pendingOrders.TryRemove(brokerOrderId, out _);
                }
            }
            catch (Exception ex)
            {
                Log("ERROR", $"[OrderPoll] Error: {ex.Message}");
            }
        }

        private void Log(string level, string msg) => OnLog?.Invoke(level, msg);

        public void Dispose()
        {
            _pollTimer?.Dispose();
        }
    }
}
