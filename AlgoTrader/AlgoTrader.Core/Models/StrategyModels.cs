using AlgoTrader.Core.Enums;
using Newtonsoft.Json;

namespace AlgoTrader.Core.Models;

/// <summary>Full strategy configuration with legs, entry/exit/risk/re-entry settings.</summary>
public record StrategyConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string PortfolioId { get; set; } = string.Empty;
    public string AccountGroup { get; set; } = string.Empty;
    public BrokerType BrokerType { get; set; }
    public bool IsActive { get; set; }
    public bool IsPositional { get; set; }
    public StrategyState State { get; set; } = StrategyState.IDLE;
    public List<LegConfig> Legs { get; set; } = new();
    public EntryConfig Entry { get; set; } = new();
    public ExitConfig Exit { get; set; } = new();
    public RiskConfig Risk { get; set; } = new();
    public AdvancedOrderConfig AdvancedOrder { get; set; } = new();

    // G.2: Premium Matching
    public bool PremiumMatchingEnabled { get; set; }
    public decimal PremiumMatchMaxDiffPercent { get; set; }
    public PremiumMatchMode PremiumMatchMode { get; set; }
    public decimal PremiumMatchRangeLow { get; set; }
    public decimal PremiumMatchRangeHigh { get; set; }

    // G.3: Wait & Trade, Move SL to Cost
    public bool WaitAndTradeEnabled { get; set; }
    public WaitAndTradeType WaitAndTradeType { get; set; }
    public decimal WaitAndTradeValue { get; set; }
    public bool TradeOnlyFirstEntry { get; set; }

    public bool MoveSLToCostEnabled { get; set; }
    public decimal MoveSLToCostWhenProfitReaches { get; set; }

    // G.4: Combined Premium re-entry
    public bool CombinedReEntryEnabled { get; set; }
    public CombinedReEntryType CombinedReEntryType { get; set; }
    public int MaxCombinedReEntries { get; set; }

    // G.6: Duration / Positional
    public PositionalDurationType PositionalDurationType { get; set; }
    public int DaysBeforeExpiry { get; set; }
    public bool IsSTBT { get; set; }
    public bool IsBTST { get; set; }
    public TimeSpan? CheckConditionNextDayAfter { get; set; }

    // G.6: Auto square-off
    public bool DailyAutoSquareOffEnabled { get; set; }
    public TimeSpan DailySquareOffTime { get; set; }
    public bool ExpiryDaySquareOffEnabled { get; set; }
    public TimeSpan ExpiryDaySquareOffTime { get; set; }

    // G.9: Execution Rules
    public ExecutionRule ExecutionRule { get; set; } = ExecutionRule.LongAndShort;

    // G.10: Indicators
    public StrategyConditionSet? EntryConditions { get; set; }
    public StrategyConditionSet? ExitConditions { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Represents an active position for a specific leg.</summary>
public record LegPosition
{
    public Guid StrategyId { get; set; }
    public Guid LegId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public Exchange Exchange { get; set; }
    public BuySell BuySell { get; set; }
    public int Qty { get; set; }
    public int NetQty { get; set; }
    public decimal AvgEntryPrice { get; set; }
    public ProductType ProductType { get; set; }
}

/// <summary>Configuration for a single leg of a multi-leg strategy.</summary>
public record LegConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LegName { get; set; } = string.Empty;

    /// <summary>Ordering index for this leg.</summary>
    public int LegIndex { get; set; }

    public string Symbol { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public Exchange Exchange { get; set; }
    public InstrumentType InstrumentType { get; set; }
    public decimal Strike { get; set; }
    public DateTime Expiry { get; set; }

    /// <summary>CE, PE, FUT, or EQ.</summary>
    public string OptionType { get; set; } = string.Empty;

    public BuySell BuySell { get; set; }
    public int Qty { get; set; }
    public int LotMultiplier { get; set; } = 1;
    public OrderType OrderType { get; set; } = OrderType.MARKET;
    public ProductType ProductType { get; set; } = ProductType.NRML;
    public PriceReference PriceReference { get; set; } = PriceReference.LTP;

    /// <summary>Buffer in points added to price.</summary>
    public decimal BufferPoints { get; set; }

    /// <summary>Buffer as percentage of price.</summary>
    public decimal BufferPercent { get; set; }

    /// <summary>Stop loss in points per leg.</summary>
    public decimal SLPoints { get; set; }

    /// <summary>Stop loss as percentage per leg.</summary>
    public decimal SLPercent { get; set; }

    /// <summary>Target in points per leg.</summary>
    public decimal TargetPoints { get; set; }

    /// <summary>Target as percentage per leg.</summary>
    public decimal TargetPercent { get; set; }

    /// <summary>Whether SL is placed on broker side (SL-M order).</summary>
    public bool HasBrokerSideSL { get; set; }

    // G.1: Trailing SL per leg
    public bool TrailSLEnabled { get; set; }
    public TrailSLType TrailSLType { get; set; }
    public decimal TrailSLX { get; set; }
    public decimal TrailSLY { get; set; }
    public TrailFrequencyType TrailFrequencyType { get; set; }
    public int TrailFrequencyValue { get; set; }
    public TrailFrequencyUnit TrailFrequencyUnit { get; set; }
    public LegExitAction ExitAction { get; set; }

    // G.2: Strike Selection
    public string UnderlyingSymbol { get; set; } = string.Empty;
    public StrikeSelectionMode StrikeSelectionMode { get; set; }
    public PremiumSelectionType PremiumSelectionType { get; set; }
    public decimal PremiumTargetValue { get; set; }

    // G.4: Per-leg re-entry
    public bool ReEntryEnabled { get; set; }
    public LegReEntryType LegReEntryType { get; set; }
    public int MaxLegReEntries { get; set; }
    public LegReEntryMethod LegReEntryMethod { get; set; }
    public TimeSpan? LegReEntryOnlyAfter { get; set; }
    public TimeSpan? LegReEntryOnlyBefore { get; set; }

    // G.6: Expiry Selection
    public ExpirySelectionType ExpirySelectionType { get; set; }
    public int ExpiryOffset { get; set; }
}

/// <summary>Entry condition configuration.</summary>
public class EntryConfig
{
    public EntryType EntryType { get; set; } = EntryType.Immediate;
    public TimeSpan? EntryTime { get; set; }
    public TimeSpan? EntryWindowEnd { get; set; }

    /// <summary>LTP condition: symbol token to monitor.</summary>
    public string LTPSymbolToken { get; set; } = string.Empty;

    /// <summary>Comparison operator for LTP condition.</summary>
    public LTPOperator LTPOperator { get; set; }

    /// <summary>LTP threshold value.</summary>
    public decimal LTPThreshold { get; set; }

    public bool WaitForCandleClose { get; set; }
    public TimeFrame CandleTimeFrame { get; set; } = TimeFrame.ONE_MINUTE;
}

/// <summary>Exit condition configuration.</summary>
public class ExitConfig
{
    public bool TimeBasedExit { get; set; }
    public bool TargetExit { get; set; }
    public bool SLExit { get; set; } = true;
    public bool ManualExit { get; set; } = true;

    public TimeSpan? ExitTime { get; set; }
    public decimal GlobalTargetPoints { get; set; }
    public decimal GlobalTargetPercent { get; set; }
    public decimal GlobalSLPoints { get; set; }
    public decimal GlobalSLPercent { get; set; }
}

/// <summary>Risk management thresholds.</summary>
public class RiskConfig
{
    // Fixed MTM SL
    public bool MTMSLEnabled { get; set; }
    public RiskValueType MTMSLType { get; set; }
    public decimal MTMSLValue { get; set; }
    
    // MTM Trailing SL (X/Y mechanism)
    public bool MTMTrailSLEnabled { get; set; }
    public RiskValueType MTMTrailSLType { get; set; }
    public decimal MTMTrailSLX { get; set; }
    public decimal MTMTrailSLY { get; set; }
    public int MTMTrailFrequencyValue { get; set; }
    public TrailFrequencyUnit MTMTrailFrequencyUnit { get; set; }
    
    // MTM Target
    public bool MTMTargetEnabled { get; set; }
    public RiskValueType MTMTargetType { get; set; }
    public decimal MTMTargetValue { get; set; }
    
    // Lock Profit: When profit reaches X, lock at Y
    public bool LockProfitEnabled { get; set; }
    public RiskValueType LockProfitType { get; set; }
    public decimal LockProfitX { get; set; }
    public decimal LockProfitY { get; set; }
    
    // Trail Profit: For every A increase, trail by B
    public bool TrailProfitEnabled { get; set; }
    public RiskValueType TrailProfitType { get; set; }
    public decimal TrailProfitA { get; set; }
    public decimal TrailProfitB { get; set; }
    
    // Lock & Trail Combined (X/Y lock + A/B trail)
    public bool LockAndTrailEnabled { get; set; }
    public RiskValueType LockAndTrailType { get; set; }
    public decimal LockX { get; set; }
    public decimal LockY { get; set; }
    public decimal TrailA { get; set; }
    public decimal TrailB { get; set; }
    
    // Delayed ticks
    public int MaxLossDelayedTicks { get; set; }
    public int MaxProfitDelayedTicks { get; set; }
    
    // Time exit
    public TimeSpan? GlobalExitTime { get; set; }
}

public class AdvancedOrderConfig
{
    public OrderExecType EntryOrderType { get; set; }
    public BufferType EntryBufferType { get; set; }
    public decimal EntryBufferValue { get; set; }
    public int EntryConvertToMarketAfterSecs { get; set; }
    
    public OrderDelayType EntryDelayType { get; set; }
    public int EntryDelaySeconds { get; set; }
    
    public OrderExecType ExitOrderType { get; set; }
    public BufferType ExitBufferType { get; set; }
    public decimal ExitBufferValue { get; set; }
    public int ExitConvertToMarketAfterSecs { get; set; }
    
    public OrderDelayType ExitDelayType { get; set; }
    public int ExitDelaySeconds { get; set; }
    
    public PriceCalcFrom EntryCalcFrom { get; set; }
    public PriceCalcFrom ExitCalcFrom { get; set; }
}

public class StrategyConditionSet
{
    public ConditionLogic Logic { get; set; }  // AND or OR
    public List<StrategyCondition> Conditions { get; set; } = new();
}
 
public class StrategyCondition
{
    public ConditionType Type { get; set; }     // Indicator, LTP, Volume, Time
    public IndicatorType Indicator { get; set; }
    public int Period1 { get; set; }   // e.g., EMA period
    public int Period2 { get; set; }   // e.g., second EMA for crossover
    public int Period3 { get; set; }   // e.g., signal period for MACD
    public ConditionOperator Operator { get; set; }
    public decimal ThresholdValue { get; set; }
    public TimeFrame TimeFrame { get; set; }
    public string Symbol { get; set; } = string.Empty;         // which symbol to evaluate on
}
