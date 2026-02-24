using Cognexalgo.Core.Domain.Enums;

namespace Cognexalgo.Core.Domain.ValueObjects
{
    /// <summary>
    /// Signal/entry condition configuration stored as JSON in Strategy.
    /// </summary>
    public class SignalConfig
    {
        public List<ConditionGroup> EntryConditionGroups { get; set; } = new();
        public List<ConditionGroup> ExitConditionGroups { get; set; } = new();
        public int ScanIntervalMs { get; set; } = 1000; // default 1 second
        public bool AllowReEntry { get; set; } = false;
        public int MaxReEntries { get; set; } = 0;
    }

    /// <summary>
    /// A group of conditions combined with AND logic. Groups combine with OR.
    /// </summary>
    public class ConditionGroup
    {
        public List<Condition> Conditions { get; set; } = new();
        public LogicOperator GroupOperator { get; set; } = LogicOperator.AND;
    }

    /// <summary>
    /// A single condition: [LeftIndicator] [Operator] [RightIndicator or Value]
    /// </summary>
    public class Condition
    {
        public IndicatorType LeftIndicator { get; set; }
        public int LeftPeriod { get; set; } = 14;
        public TimeFrame LeftTimeFrame { get; set; } = TimeFrame.Min1;

        public Comparator Operator { get; set; }

        public ValueSource RightSource { get; set; } = ValueSource.StaticValue;
        public IndicatorType RightIndicator { get; set; }
        public int RightPeriod { get; set; }
        public TimeFrame RightTimeFrame { get; set; } = TimeFrame.Min1;

        public double StaticValue { get; set; }
    }

    /// <summary>
    /// RMS configuration stored as JSON in Strategy.
    /// </summary>
    public class RmsConfig
    {
        // Strategy-level
        public decimal MaxLoss { get; set; }          // ₹ — auto-exit all legs if hit
        public decimal MaxProfit { get; set; }         // ₹ — auto-exit all legs if hit
        public decimal TrailingSL { get; set; }        // ₹ or % of peak
        public bool TrailingIsPercent { get; set; } = false;

        public decimal LockProfitAt { get; set; }      // When PnL hits X
        public decimal LockProfitTo { get; set; }      // Lock minimum at Y

        public int MaxOrdersPerDay { get; set; }       // Stop after N orders
        public int MaxReEntries { get; set; }          // Max N re-entries after SL

        public string? TimeBasedExitTime { get; set; } // "15:15" — force exit at HH:MM
        public bool HasExpiryDayRules { get; set; } = false;
    }

    /// <summary>
    /// Execution configuration stored as JSON in Strategy.
    /// </summary>
    public class ExecutionConfig
    {
        public string EntryTime { get; set; } = "09:20";
        public string ExitTime { get; set; } = "15:15";
        public ProductType ProductType { get; set; } = ProductType.MIS;
        public OrderType DefaultOrderType { get; set; } = OrderType.MARKET;

        // Slippage for paper trading
        public double SlippagePercent { get; set; } = 0.05;

        // Parallel exit
        public bool ParallelExitEnabled { get; set; } = true;
    }

    /// <summary>
    /// Strategy performance metrics, updated in real-time.
    /// </summary>
    public class StrategyMetrics
    {
        public decimal TotalPnl { get; set; }
        public decimal UnrealizedPnl { get; set; }
        public decimal RealizedPnl { get; set; }
        public decimal MaxDrawdown { get; set; }
        public decimal HighWatermark { get; set; }

        public int TotalOrders { get; set; }
        public int FilledOrders { get; set; }
        public int RejectedOrders { get; set; }

        public int WinCount { get; set; }
        public int LossCount { get; set; }
        public double WinRate => TotalOrders > 0 ? (double)WinCount / (WinCount + LossCount) * 100 : 0;

        public int ConsecutiveErrors { get; set; }
        public DateTime? LastSignalTime { get; set; }
        public DateTime? LastOrderTime { get; set; }
    }
}
