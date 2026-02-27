namespace AlgoTrader.Core.Enums;

/// <summary>Supported broker integrations.</summary>
public enum BrokerType
{
    AngelOne,
    Zerodha,
    Fyers,
    AliceBlue,
    Finvasia,
    Sharekhan,
    XTS
}

/// <summary>Indian stock exchanges.</summary>
public enum Exchange
{
    NSE,
    BSE,
    NFO,
    CDS,
    MCX,
    BFO
}

/// <summary>Instrument types for trading.</summary>
public enum InstrumentType
{
    EQ,
    FUT,
    CE,
    PE,
    INDEX
}

/// <summary>Order type sent to broker.</summary>
public enum OrderType
{
    MARKET,
    LIMIT,
    SL,
    SL_M
}

/// <summary>Product type for margin treatment.</summary>
public enum ProductType
{
    CNC,
    NRML,
    MIS,
    BO,
    CO
}

/// <summary>Transaction direction.</summary>
public enum BuySell
{
    BUY,
    SELL
}

/// <summary>Current order status on the exchange.</summary>
public enum OrderStatus
{
    OPEN,
    COMPLETE,
    CANCELLED,
    REJECTED,
    TRIGGER_PENDING,
    MODIFIED
}

/// <summary>Lifecycle state of a strategy instance.</summary>
public enum StrategyState
{
    IDLE,
    WAITING_ENTRY,
    ENTERED,
    EXITED,
    ERROR
}

/// <summary>WebSocket subscription depth.</summary>
public enum SubscriptionMode
{
    LTP = 1,
    QUOTE = 2,
    FULL = 3
}

/// <summary>Candle aggregation timeframe.</summary>
public enum TimeFrame
{
    ONE_MINUTE,
    THREE_MINUTE,
    FIVE_MINUTE,
    FIFTEEN_MINUTE,
    THIRTY_MINUTE,
    ONE_HOUR,
    ONE_DAY
}

/// <summary>Reason why a strategy exited its position.</summary>
public enum ExitReason
{
    Target,
    StopLoss,
    TimeBasedExit,
    MaxProfit,
    MaxLoss,
    ProfitLock,
    Manual,
    UnderlyingMove
}

/// <summary>Type of entry condition for a strategy.</summary>
public enum EntryType
{
    Immediate,
    LTPCondition,
    TimeBased,
    CandleClose,
    Manual
}

/// <summary>LTP comparison operator.</summary>
public enum LTPOperator
{
    GreaterEqual,
    LessEqual
}

/// <summary>Price reference for order pricing.</summary>
public enum PriceReference
{
    LTP,
    BestBid,
    BestAsk,
    Market,
    AvgBidAsk
}

/// <summary>Re-entry trigger type.</summary>
public enum ReEntryType
{
    OnLTP,
    OnCandleClose,
    OnSLExecution,
    OnTargetExecution
}

// G.1 Enums
public enum TrailSLType { Percentage, Points }
public enum TrailFrequencyType { OnEveryMove, TimeBased }
public enum TrailFrequencyUnit { Minutes, Seconds }
public enum LegExitAction { SquareOffLeg, SquareOffAll }

// G.2 Enums
public enum StrikeSelectionMode { ByLevel, ByPremium }
public enum PremiumSelectionType { CloseTo, GreaterThan, LessThan }
public enum PremiumMatchMode { CloseTo, Range }

// G.3 Enums
public enum WaitAndTradeType { PercentUp, PercentDown, PointsUp, PointsDown, Immediate }

// G.4 Enums
public enum LegReEntryType { ReEnterAtCost, ReverseAndReEnterAtCost, ReEnterImmediately, ReverseAndReEnterImmediately }
public enum LegReEntryMethod { LTP, CandleClose }
public enum CombinedReEntryType { ReEnterImmediately, ReverseAndReEnterImmediately }

// G.5 Enums
public enum RiskValueType { Percentage, Amount }

// G.6 Enums
public enum PositionalDurationType { Intraday, NightPosition, NBeforeExpiry }
public enum ExpirySelectionType { Nearest, Next, WeeklyNearest, MonthlyNearest, Fixed }

// G.7 Enums
public enum OrderExecType { Market, Limit, SL_Limit }
public enum BufferType { Percentage, Points }
public enum OrderDelayType { NoDelay, DelayBuyPositions, DelaySellPositions, DelayAll }
public enum PriceCalcFrom { AveragePrice, LTP }

// G.9 Enums
public enum ExecutionRule { LongOnly, ShortOnly, LongAndShort, Stop, Reversal }

// G.10 Enums
public enum ConditionLogic { AND, OR }
public enum ConditionType { Indicator, PriceLevel, VolumeSpike, Time }
public enum IndicatorType { EMA, SMA, RSI, MACD_Histogram, MACD_Signal, MACD_Line, BollingerUpper, BollingerLower, BollingerMid, ATR, Supertrend, VWAP, ADX, Stochastic_K, Stochastic_D }
public enum ConditionOperator { CrossesAbove, CrossesBelow, IsAbove, IsBelow, IsGreaterThan, IsLessThan, IsOversold, IsOverbought, IsBullish, IsBearish }
