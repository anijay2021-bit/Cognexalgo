namespace Cognexalgo.Core.Domain.Enums
{
    /// <summary>
    /// Strategy type codes used in StrategyId generation.
    /// Format: STR-{YYYYMMDD}-{TYPE}-{SEQ}
    /// </summary>
    public enum StrategyType
    {
        /// <summary>Straddle: ATM CE + ATM PE same strike</summary>
        STRD,
        /// <summary>Strangle: OTM CE + OTM PE configurable offset</summary>
        STNG,
        /// <summary>Iron Condor: 4-leg spread</summary>
        CNDL,
        /// <summary>Custom indicator-based strategy</summary>
        CSTM,
        /// <summary>Pre-built logic strategy</summary>
        PRBL,
        /// <summary>Scripted strategy</summary>
        SCPT
    }

    public enum StrategyStatus
    {
        Draft,
        Active,
        Paused,
        Completed,
        Error
    }

    public enum TradingMode
    {
        PaperTrade,
        LiveTrade
    }

    public enum Direction
    {
        BUY,
        SELL
    }

    public enum OrderType
    {
        MARKET,
        LIMIT,
        SL,
        SL_M
    }

    public enum ProductType
    {
        MIS,
        NRML,
        CNC
    }

    public enum OrderStatus
    {
        PENDING,
        PLACED,
        OPEN,
        COMPLETE,
        CANCELLED,
        REJECTED
    }

    public enum InstrumentType
    {
        OPTIDX,   // Index Options
        FUTIDX,   // Index Futures
        OPTSTK,   // Stock Options
        FUTSTK,   // Stock Futures
        EQ        // Equity Cash
    }

    public enum SignalType
    {
        Entry,
        Exit,
        ReEntry,
        ForceExit
    }

    public enum SignalActionTaken
    {
        OrderPlaced,
        Suppressed,
        RMSBlocked
    }

    public enum LegStatus
    {
        PENDING,
        OPEN,
        IN_POSITION,
        EXITED,
        CANCELLED,
        ERROR
    }

    public enum ExitReason
    {
        StopLoss,
        Target,
        TrailingSL,
        LockProfit,
        TimeExit,
        ManualExit,
        RMSBreach,
        ExpiryExit,
        SignalExit
    }
}
