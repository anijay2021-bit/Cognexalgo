using System;
using System.Collections.Generic;
using System.Linq;
using Cognexalgo.Core.Domain.Enums;
using Cognexalgo.Core.Domain.ValueObjects;

namespace Cognexalgo.Core.Domain.Indicators
{
    /// <summary>
    /// Condition Builder (Module 2B):
    /// Evaluates multi-condition signal rules with AND/OR logic groups.
    /// 
    /// Architecture:
    /// - ConditionGroup = List of Conditions combined with AND
    /// - Multiple ConditionGroups combine with OR
    /// - (Group1.A AND Group1.B) OR (Group2.A AND Group2.B)
    /// 
    /// Supports all operators: CrossesAbove, CrossesBelow, IsAbove, IsBelow,
    /// IncreasesBy, DecreasesBy, IsOverbought, IsOversold
    /// 
    /// [DIAGNOSTIC] Supports optional logging callback for condition evaluation tracing.
    /// </summary>
    public class ConditionEvaluator
    {
        private readonly IndicatorEngine _engine;
        private readonly Action<string>? _diagnosticLog;
        private DateTime _lastDiagnosticTime = DateTime.MinValue;
        private static readonly TimeSpan DiagnosticInterval = TimeSpan.FromSeconds(10);

        public ConditionEvaluator(IndicatorEngine engine, Action<string>? diagnosticLog = null)
        {
            _engine = engine;
            _diagnosticLog = diagnosticLog;
        }

        /// <summary>
        /// Evaluate a full signal config (multiple condition groups with OR logic).
        /// Returns true if ANY group evaluates to true (each group uses AND logic internally).
        /// </summary>
        public bool Evaluate(List<ConditionGroup> groups, out string triggerDescription)
        {
            triggerDescription = string.Empty;
            if (groups == null || !groups.Any()) return false;

            bool shouldLog = _diagnosticLog != null && (DateTime.Now - _lastDiagnosticTime) > DiagnosticInterval;

            for (int i = 0; i < groups.Count; i++)
            {
                var (passed, desc, failedCondition) = EvaluateGroupWithDiag(groups[i]);
                if (passed)
                {
                    triggerDescription = desc;
                    if (shouldLog)
                    {
                        _diagnosticLog?.Invoke($"✅ SIGNAL MATCH — Group {i + 1}: {desc}");
                        _lastDiagnosticTime = DateTime.Now;
                    }
                    return true;
                }
                else if (shouldLog)
                {
                    _diagnosticLog?.Invoke($"⛔ Group {i + 1}/{groups.Count} FAILED at: {failedCondition}");
                }
            }

            if (shouldLog)
            {
                _diagnosticLog?.Invoke($"── No groups matched ({groups.Count} evaluated) ──");
                _lastDiagnosticTime = DateTime.Now;
            }

            return false;
        }

        /// <summary>
        /// Evaluate a single condition group with diagnostic output.
        /// </summary>
        private (bool Passed, string Description, string FailedAt) EvaluateGroupWithDiag(ConditionGroup group)
        {
            if (group.Conditions == null || !group.Conditions.Any())
                return (false, "", "Empty group");

            var descriptions = new List<string>();

            foreach (var condition in group.Conditions)
            {
                var (passed, desc) = EvaluateCondition(condition);
                if (!passed)
                    return (false, "", desc); // AND logic: one failure = entire group fails
                descriptions.Add(desc);
            }

            return (true, string.Join(" AND ", descriptions), "");
        }

        /// <summary>
        /// Evaluate a single condition group (all conditions must pass = AND logic).
        /// </summary>
        public (bool Passed, string Description) EvaluateGroup(ConditionGroup group)
        {
            var (passed, desc, _) = EvaluateGroupWithDiag(group);
            return (passed, desc);
        }

        /// <summary>
        /// Evaluate a single condition.
        /// </summary>
        public (bool Passed, string Description) EvaluateCondition(Condition condition)
        {
            // Get left-hand value
            double leftValue = _engine.GetValue(
                condition.LeftIndicator, condition.LeftPeriod, condition.LeftTimeFrame, 0);

            // Build names early for diagnostic output
            string leftName = $"{condition.LeftIndicator}({condition.LeftPeriod})";
            string rightName = condition.RightSource == ValueSource.StaticValue
                ? condition.StaticValue.ToString("F2")
                : $"{condition.RightIndicator}({condition.RightPeriod})";

            if (double.IsNaN(leftValue))
                return (false, $"{leftName} = NaN (not available)");

            // Get right-hand value
            double rightValue = condition.RightSource == ValueSource.StaticValue
                ? condition.StaticValue
                : _engine.GetValue(
                    condition.RightIndicator, condition.RightPeriod, condition.RightTimeFrame, 0);

            if (double.IsNaN(rightValue) && condition.RightSource != ValueSource.StaticValue)
                return (false, $"{rightName} = NaN (not available)");

            bool result = false;

            switch (condition.Operator)
            {
                // ─── Direct Comparisons ──────────────────────────
                case Comparator.IsAbove:
                case Comparator.GREATER_THAN:
                case Comparator.CLOSES_ABOVE:
                    result = leftValue > rightValue;
                    break;

                case Comparator.IsBelow:
                case Comparator.LESS_THAN:
                case Comparator.CLOSES_BELOW:
                    result = leftValue < rightValue;
                    break;

                case Comparator.IsEqual:
                    result = Math.Abs(leftValue - rightValue) < 0.0001;
                    break;

                // ─── Crossover (requires previous bar) ──────────
                case Comparator.CrossesAbove:
                case Comparator.CROSS_ABOVE:
                {
                    double prevLeft = _engine.GetValue(
                        condition.LeftIndicator, condition.LeftPeriod, condition.LeftTimeFrame, 1);
                    double prevRight = condition.RightSource == ValueSource.StaticValue
                        ? condition.StaticValue
                        : _engine.GetValue(
                            condition.RightIndicator, condition.RightPeriod, condition.RightTimeFrame, 1);

                    if (!double.IsNaN(prevLeft) && !double.IsNaN(prevRight))
                        result = (leftValue > rightValue) && (prevLeft <= prevRight);
                    break;
                }

                case Comparator.CrossesBelow:
                case Comparator.CROSS_BELOW:
                {
                    double prevLeft = _engine.GetValue(
                        condition.LeftIndicator, condition.LeftPeriod, condition.LeftTimeFrame, 1);
                    double prevRight = condition.RightSource == ValueSource.StaticValue
                        ? condition.StaticValue
                        : _engine.GetValue(
                            condition.RightIndicator, condition.RightPeriod, condition.RightTimeFrame, 1);

                    if (!double.IsNaN(prevLeft) && !double.IsNaN(prevRight))
                        result = (leftValue < rightValue) && (prevLeft >= prevRight);
                    break;
                }

                // ─── Change-based ────────────────────────────────
                case Comparator.IncreasesBy:
                {
                    double prevLeft = _engine.GetValue(
                        condition.LeftIndicator, condition.LeftPeriod, condition.LeftTimeFrame, 1);
                    if (!double.IsNaN(prevLeft))
                        result = (leftValue - prevLeft) >= rightValue;
                    break;
                }

                case Comparator.DecreasesBy:
                {
                    double prevLeft = _engine.GetValue(
                        condition.LeftIndicator, condition.LeftPeriod, condition.LeftTimeFrame, 1);
                    if (!double.IsNaN(prevLeft))
                        result = (prevLeft - leftValue) >= rightValue;
                    break;
                }

                // ─── Overbought / Oversold (RSI-style) ──────────
                case Comparator.IsOverbought:
                    result = leftValue >= (rightValue > 0 ? rightValue : 70);
                    break;

                case Comparator.IsOversold:
                    result = leftValue <= (rightValue > 0 ? rightValue : 30);
                    break;
            }

            string resultIcon = result ? "✓" : "✗";
            string desc = $"{resultIcon} {leftName} [{leftValue:F2}] {condition.Operator} {rightName} [{rightValue:F2}]";
            return (result, desc);
        }
    }
}

