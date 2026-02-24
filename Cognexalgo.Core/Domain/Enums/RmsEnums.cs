namespace Cognexalgo.Core.Domain.Enums
{
    /// <summary>
    /// Types of RMS rules that can be configured.
    /// </summary>
    public enum RmsRuleType
    {
        MaxLoss,
        MaxProfit,
        TrailingSL,
        LockProfit,
        MaxOrdersPerDay,
        MaxReEntries,
        TimeBasedExit,
        ExpiryDayRules,
        DailyMaxLoss,
        DailyMaxProfit,
        MaxConcurrentStrategies,
        MaxMarginUtilization,
        KillSwitch
    }

    /// <summary>
    /// RMS breach action taken.
    /// </summary>
    public enum RmsAction
    {
        ExitAllLegs,
        PauseStrategy,
        HaltAllStrategies,
        AlertOnly,
        BlockOrder
    }

    /// <summary>
    /// Strike selection methods for options strategies.
    /// </summary>
    public enum StrikeSelectionType
    {
        ATM,
        PremiumMatch,
        Delta,
        OffsetFromATM
    }

    /// <summary>
    /// Adjustment triggers for dynamic leg management.
    /// </summary>
    public enum AdjustmentTriggerType
    {
        None,
        PremiumDifference,
        LegStopLoss,
        CombinedMTM
    }

    /// <summary>
    /// Actions when an adjustment is triggered.
    /// </summary>
    public enum AdjustmentAction
    {
        ShiftWhole,
        ExitLoser,
        AddCover
    }
}
