using AlgoTrader.Core.Enums;
using AlgoTrader.Core.Interfaces;
using AlgoTrader.Brokers.AngelOne;

namespace AlgoTrader.MarketData;

public class ExpiryResolver
{
    private readonly InstrumentMasterService _instruments;
    
    public ExpiryResolver(InstrumentMasterService instruments)
    {
        _instruments = instruments;
    }

    public async Task<DateTime> ResolveExpiryAsync(
        string symbol, Exchange exchange, ExpirySelectionType type, int offset = 0)
    {
        var expiries = _instruments.GetExpiries(symbol, exchange);
        var future = expiries.Where(e => e >= DateTime.Today).OrderBy(e => e).ToList();
        
        return type switch
        {
            ExpirySelectionType.Nearest  => future.ElementAtOrDefault(offset),
            ExpirySelectionType.Next     => future.ElementAtOrDefault(1 + offset),
            ExpirySelectionType.WeeklyNearest  => future.FirstOrDefault(e => e.DayOfWeek == DayOfWeek.Thursday),
            ExpirySelectionType.MonthlyNearest => future.FirstOrDefault(e => e.Day >= 25 && e.DayOfWeek == DayOfWeek.Thursday),
            ExpirySelectionType.Fixed    => future.FirstOrDefault(), // uses LegConfig.Expiry directly
            _ => future.FirstOrDefault()
        };
    }
    
    public bool IsExpiryDay(DateTime expiry) => expiry.Date == DateTime.Today;
    
    public int DaysToExpiry(DateTime expiry) => (expiry.Date - DateTime.Today).Days;
}
