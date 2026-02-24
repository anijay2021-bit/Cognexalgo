using System;

namespace Cognexalgo.Core.Rules
{
    public class RuleEvaluator
    {
        public bool Evaluate(Rule rule, EvaluationContext context, string strategyName = "Unknown", Action<string> logAction = null)
        {
            // Default: All conditions must be Met (AND logic)
            // You could extend this to support OR groups later
            foreach (var condition in rule.Conditions)
            {
                if (!EvaluateCondition(condition, context, strategyName, logAction))
                {
                    return false;
                }
            }
            return true;
        }

        private bool EvaluateCondition(Condition condition, EvaluationContext context, string strategyName = "Unknown", Action<string> logAction = null)
        {
            // 1. Get Current Values (Offset 0)
            double leftValue = GetValue(condition.Indicator, condition.Period, context, 0);
            double rightValue = GetRightValue(condition, context, 0);

            bool result = false;
            // 2. Check Standard Operators
            switch (condition.Operator)
            {
                case Comparator.GREATER_THAN: result = leftValue > rightValue; break;
                case Comparator.LESS_THAN: result = leftValue < rightValue; break;
                case Comparator.EQUALS: result = Math.Abs(leftValue - rightValue) < 0.0001; break;
                
                // 3. Check Crossover / Closes Operators (Requires Previous Values)
                case Comparator.CROSS_ABOVE:
                case Comparator.CLOSES_ABOVE:
                case Comparator.CROSS_BELOW:
                case Comparator.CLOSES_BELOW:
                    double prevLeft = GetValue(condition.Indicator, condition.Period, context, 1);
                    double prevRight = GetRightValue(condition, context, 1);
                    
                    if (condition.Operator == Comparator.CROSS_ABOVE || condition.Operator == Comparator.CLOSES_ABOVE)
                    {
                        result = (leftValue > rightValue) && (prevLeft <= prevRight);
                    }
                    else // CROSS_BELOW or CLOSES_BELOW
                    {
                        result = (leftValue < rightValue) && (prevLeft >= prevRight);
                    }
                    break;
            }

            if (!result)
            {
                string leftName = $"{condition.Indicator}({condition.Period})";
                string rightName = condition.SourceType == ValueSource.StaticValue ? condition.StaticValue.ToString() : $"{condition.RightIndicator}({condition.RightPeriod})";
                string logMsg = $"[{strategyName}] Condition Failed: {leftName} [{leftValue:F2}] {condition.Operator} {rightName} [{rightValue:F2}]";
                // Console.WriteLine(logMsg);
                logAction?.Invoke(logMsg);
            }
            else
            {
                string leftName = $"{condition.Indicator}({condition.Period})";
                string rightName = condition.SourceType == ValueSource.StaticValue ? condition.StaticValue.ToString() : $"{condition.RightIndicator}({condition.RightPeriod})";
                string logMsg = $"[{strategyName}] Condition Passed: {leftName} [{leftValue:F2}] {condition.Operator} {rightName} [{rightValue:F2}]";
                // Console.WriteLine(logMsg);
                logAction?.Invoke(logMsg);
            }

            return result;
        }

        private double GetRightValue(Condition condition, EvaluationContext context, int offset)
        {
            if (condition.SourceType == ValueSource.StaticValue) return condition.StaticValue;
            // Both Indicator and TrendFilter use the RightIndicator/RightPeriod logic
            return GetValue(condition.RightIndicator, condition.RightPeriod, context, offset);
        }

        private double GetValue(IndicatorType type, int period, EvaluationContext context, int offset)
        {
            return context.GetIndicatorValue(type, period, offset);
        }
    }
}
