using AlgoTrader.Core.Models;
using AlgoTrader.Core.Enums;

namespace AlgoTrader.RMS;

public class PortfolioProfitManager
{
    private decimal _highestMTM = 0;
    private decimal _lockedProfit = decimal.MinValue;
    private decimal _trailSLPrice = decimal.MinValue;
    private DateTime _lastTrailTime = DateTime.MinValue;
    
    // Returns true if strategy should exit, with reason
    public (bool ShouldExit, string Reason) Evaluate(
        RiskConfig risk, decimal currentMTM, DateTime now)
    {
        // Update highest MTM seen
        if (currentMTM > _highestMTM) _highestMTM = currentMTM;
        
        // 1. Fixed MTM SL
        if (risk.MTMSLEnabled && currentMTM <= -ResolveValue(risk.MTMSLType, risk.MTMSLValue, currentMTM))
            return (true, "MTM StopLoss hit");
        
        // 2. MTM Target
        if (risk.MTMTargetEnabled && currentMTM >= ResolveValue(risk.MTMTargetType, risk.MTMTargetValue, currentMTM))
            return (true, "MTM Target hit");
        
        // 3. MTM Trailing SL (X/Y)
        if (risk.MTMTrailSLEnabled)
        {
            decimal x = ResolveValue(risk.MTMTrailSLType, risk.MTMTrailSLX, _highestMTM);
            if (_highestMTM >= x)
            {
                bool timeOk = CheckTrailFrequency(risk, now);
                if (timeOk)
                {
                    decimal y = ResolveValue(risk.MTMTrailSLType, risk.MTMTrailSLY, _highestMTM);
                    decimal newSL = _highestMTM - y;
                    if (newSL > _trailSLPrice) { _trailSLPrice = newSL; _lastTrailTime = now; }
                }
                if (_trailSLPrice > decimal.MinValue && currentMTM <= _trailSLPrice)
                    return (true, $"MTM Trailing SL hit at ₹{_trailSLPrice:N0}");
            }
        }
        
        // 4. Lock Profit
        if (risk.LockProfitEnabled && _lockedProfit == decimal.MinValue)
        {
            decimal x = ResolveValue(risk.LockProfitType, risk.LockProfitX, currentMTM);
            if (currentMTM >= x)
            {
                _lockedProfit = ResolveValue(risk.LockProfitType, risk.LockProfitY, currentMTM);
            }
        }
        if (_lockedProfit > decimal.MinValue && currentMTM <= _lockedProfit)
            return (true, $"Locked profit breached at ₹{_lockedProfit:N0}");
        
        // 5. Trail Profit (A/B): for every A increase, trail by B
        if (risk.TrailProfitEnabled && currentMTM > 0)
        {
            decimal a = ResolveValue(risk.TrailProfitType, risk.TrailProfitA, currentMTM);
            decimal b = ResolveValue(risk.TrailProfitType, risk.TrailProfitB, currentMTM);
            if (a > 0)
            {
                int steps = (int)(_highestMTM / a);
                decimal trailFloor = (steps * a) - b;
                if (trailFloor > _trailSLPrice) _trailSLPrice = trailFloor;
                if (_trailSLPrice > 0 && currentMTM <= _trailSLPrice)
                    return (true, $"Trail Profit floor hit at ₹{_trailSLPrice:N0}");
            }
        }
        
        // 6. Lock & Trail Combined
        if (risk.LockAndTrailEnabled)
        {
            decimal x = ResolveValue(risk.LockAndTrailType, risk.LockX, currentMTM);
            if (_lockedProfit == decimal.MinValue && currentMTM >= x)
                _lockedProfit = ResolveValue(risk.LockAndTrailType, risk.LockY, currentMTM);
            
            if (_lockedProfit > decimal.MinValue)
            {
                decimal a = ResolveValue(risk.LockAndTrailType, risk.TrailA, _highestMTM);
                decimal b = ResolveValue(risk.LockAndTrailType, risk.TrailB, _highestMTM);
                if (a > 0)
                {
                    int steps = Math.Max(0, (int)((_highestMTM - x) / a));
                    decimal newLocked = _lockedProfit + (steps * b);
                    if (newLocked > _lockedProfit) _lockedProfit = newLocked;
                    if (currentMTM <= _lockedProfit)
                        return (true, $"Lock+Trail floor hit at ₹{_lockedProfit:N0}");
                }
            }
        }
        
        return (false, string.Empty);
    }
    
    private bool CheckTrailFrequency(RiskConfig risk, DateTime now)
    {
        if (risk.MTMTrailFrequencyValue == 0) return true;
        var interval = risk.MTMTrailFrequencyUnit == TrailFrequencyUnit.Seconds
            ? TimeSpan.FromSeconds(risk.MTMTrailFrequencyValue)
            : TimeSpan.FromMinutes(risk.MTMTrailFrequencyValue);
        return (now - _lastTrailTime) >= interval;
    }
    
    private decimal ResolveValue(RiskValueType type, decimal config, decimal reference)
        => type == RiskValueType.Percentage ? reference * config / 100 : config;
    
    public void Reset() { _highestMTM=0; _lockedProfit=decimal.MinValue; _trailSLPrice=decimal.MinValue; }
}
