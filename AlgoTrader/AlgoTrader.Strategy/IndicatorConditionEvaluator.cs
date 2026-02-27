using System.Collections.Concurrent;
using AlgoTrader.Core.Models;
using AlgoTrader.Core.Enums;
using AlgoTrader.MarketData;
using AlgoTrader.Core.Interfaces;

namespace AlgoTrader.Strategy;

public class IndicatorConditionEvaluator
{
    private readonly HistoricalDataManager _historical;
    private readonly AlgoTrader.MarketData.IndicatorEngine _indicators; // Fixed namespace since it's now in MarketData

    // Cache: symbol+timeframe+indicator → last N computed values
    private readonly ConcurrentDictionary<string, Queue<decimal>> _valueCache = new();
 
    public IndicatorConditionEvaluator(
        HistoricalDataManager historical,
        AlgoTrader.MarketData.IndicatorEngine indicators)
    {
        _historical = historical;
        _indicators = indicators;
    }

    public async Task<bool> EvaluateSetAsync(
        StrategyConditionSet conditionSet,
        string symbol, string token, Exchange exchange,
        AccountCredential account)
    {
        var results = new List<bool>();
        foreach (var cond in conditionSet.Conditions)
        {
            bool result = await EvaluateOneAsync(cond, symbol, token, exchange, account);
            results.Add(result);
        }
        return conditionSet.Logic == ConditionLogic.AND
            ? results.All(r => r)
            : results.Any(r => r);
    }
 
    private async Task<bool> EvaluateOneAsync(
        StrategyCondition cond, string symbol, string token,
        Exchange exchange, AccountCredential account)
    {
        // Get or refresh candles (last 200 bars for indicator calculation)
        var candles = await _historical.GetCandlesWithCacheAsync(symbol, token, exchange, cond.TimeFrame, DateTime.Now.AddDays(-10), DateTime.Now, account);
        if (candles.Count < 50) return false;
 
        var key = $"{symbol}_{cond.TimeFrame}_{cond.Indicator}_{cond.Period1}_{cond.Period2}";
        
        switch (cond.Indicator)
        {
            case IndicatorType.EMA:
                var ema = (await _indicators.CalcEMAAsync(candles, cond.Period1)).ToList();
                var prev = ema.Count >= 2 ? ema[^2].Ema ?? 0 : 0;
                var curr = ema.Last().Ema ?? 0;
                
                decimal threshold = cond.ThresholdValue;
                if (cond.Period2 > 0)
                {
                    var ema2 = (await _indicators.CalcEMAAsync(candles, cond.Period2)).ToList();
                    threshold = (decimal)(ema2.Last().Ema ?? 0);
                }

                return EvaluateOperator(cond.Operator, (decimal)curr, (decimal)prev, threshold);
            
            case IndicatorType.RSI:
                var rsi = (await _indicators.CalcRSIAsync(candles, cond.Period1)).ToList();
                var rsiVal = (decimal)(rsi.Last().Rsi ?? 50);
                if (cond.Operator == ConditionOperator.IsOversold) return rsiVal < 30;
                if (cond.Operator == ConditionOperator.IsOverbought) return rsiVal > 70;
                return EvaluateOperator(cond.Operator, rsiVal, 0, cond.ThresholdValue);
            
            case IndicatorType.MACD_Histogram:
                var macd = (await _indicators.CalcMACDAsync(candles, 12, 26, 9)).ToList();
                var hist = (decimal)(macd.Last().Histogram ?? 0);
                var prevHist = (decimal)(macd.Count >= 2 ? macd[^2].Histogram ?? 0 : 0);
                if (cond.Operator == ConditionOperator.IsBullish) return hist > 0;
                if (cond.Operator == ConditionOperator.IsBearish) return hist < 0;
                if (cond.Operator == ConditionOperator.CrossesAbove) return prevHist <= 0 && hist > 0;
                if (cond.Operator == ConditionOperator.CrossesBelow) return prevHist >= 0 && hist < 0;
                return EvaluateOperator(cond.Operator, hist, 0, cond.ThresholdValue);
            
            case IndicatorType.BollingerUpper:
            case IndicatorType.BollingerLower:
            case IndicatorType.BollingerMid:
                var bb = (await _indicators.CalcBollingerAsync(candles, cond.Period1, 2.0)).ToList();
                var lastBB = bb.Last();
                var bbVal = cond.Indicator == IndicatorType.BollingerUpper 
                    ? (decimal)(lastBB.UpperBand ?? 0)
                    : cond.Indicator == IndicatorType.BollingerLower 
                        ? (decimal)(lastBB.LowerBand ?? 0)
                        : (decimal)(lastBB.Sma ?? 0);
                return EvaluateOperator(cond.Operator, (decimal)candles.Last().Close, 0, bbVal);
            
            default: return false;
        }
    }
 
    private bool EvaluateOperator(ConditionOperator op, decimal curr, decimal prev, decimal threshold)
        => op switch
        {
            ConditionOperator.CrossesAbove  => prev <= threshold && curr > threshold,
            ConditionOperator.CrossesBelow  => prev >= threshold && curr < threshold,
            ConditionOperator.IsAbove       => curr > threshold,
            ConditionOperator.IsBelow       => curr < threshold,
            ConditionOperator.IsGreaterThan => curr > threshold,
            ConditionOperator.IsLessThan    => curr < threshold,
            _ => false
        };
}
