using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cognexalgo.Core.Models;
using Cognexalgo.Core.Services;
using Newtonsoft.Json;

namespace Cognexalgo.Core.Strategies
{
    public class CalendarStrategyConfig
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Symbol { get; set; } = "NIFTY";
        
        // Changed to string for easier JSON binding, parsed in Init
        public string CalendarEntryTime { get; set; } = "15:26";
        public string RollTime { get; set; } = "15:24"; 
        
        public double BuyStopLossPercent { get; set; } = 6.0;
        public int TotalLots { get; set; } = 1;
        public string ProductType { get; set; } = "NRML";
        public double ShortStraddleCombinedSL { get; set; } = 20.0; 

        // Parsed TimeSpans
        [JsonIgnore]
        public TimeSpan EntryTimeSpan { get; set; }
        [JsonIgnore]
        public TimeSpan RollTimeSpan { get; set; }
    }

    public class CalendarStrategy : StrategyBase
    {
        private CalendarStrategyConfig _config;
        private List<StrategyLeg> _activeLegs = new List<StrategyLeg>();
        private bool _isReversalActive = false;

        public CalendarStrategy(TradingEngine engine, string jsonConfig) : base(engine, "Calendar")
        {
            Initialize(jsonConfig);
        }

        public void Initialize(string jsonConfig)
        {
            try 
            {
                // 1. Deserialize the Dictionary first (as saved by ViewModel)
                var rawParams = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonConfig);
                
                // 2. Map to Config Object
                _config = new CalendarStrategyConfig();
                if (rawParams != null)
                {
                     if (rawParams.ContainsKey("CalendarEntryTime")) _config.CalendarEntryTime = rawParams["CalendarEntryTime"];
                     if (rawParams.ContainsKey("WeeklyExitTime")) _config.RollTime = rawParams["WeeklyExitTime"]; // Mapped to RollTime
                     if (rawParams.ContainsKey("BuyStopLossPercentage")) _config.BuyStopLossPercent = double.Parse(rawParams["BuyStopLossPercentage"]);
                     // ... map others or use defaults
                }
                
                // 3. Parse TimeSpans
                _config.EntryTimeSpan = TimeSpan.Parse(_config.CalendarEntryTime);
                _config.RollTimeSpan = TimeSpan.Parse(_config.RollTime);

                Console.WriteLine($"[Calendar] Initialized: Entry={_config.EntryTimeSpan}, Roll={_config.RollTimeSpan}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Calendar] Init Error: {ex.Message}");
                // Fallback to defaults
                _config = new CalendarStrategyConfig();
                _config.EntryTimeSpan = new TimeSpan(15, 26, 0);
                _config.RollTimeSpan = new TimeSpan(15, 24, 0);
            }
        }

        public override async Task OnTickAsync(TickerData ticker)
        {
            if (!IsActive || _config == null) return;

            var now = DateTime.Now;
            var time = now.TimeOfDay;

            // 1. ROLLING LOGIC (Tuesday @ RollTime)
            if (now.DayOfWeek == DayOfWeek.Tuesday && time >= _config.RollTimeSpan && time < _config.EntryTimeSpan)
            {
                await ManageWeeklyRoll(ticker);
            }

            // 2. ENTRY LOGIC (Tuesday @ EntryTime)
            // Check if we already have positions
            if (!_activeLegs.Any() && now.DayOfWeek == DayOfWeek.Tuesday && time >= _config.EntryTimeSpan)
            {
                 await ExecuteCalendarEntry(ticker);
            }



            // 3. RISK MANAGEMENT (Reversal Chain)
            if (_activeLegs.Any())
            {
                await ManageRisk(ticker);
            }
        }

        private async Task ExecuteCalendarEntry(TickerData ticker)
        {
            double spot = GetLtp(ticker, _config.Symbol);
            if (spot <= 0) return;

            Console.WriteLine($"[Calendar] Executing Entry @ {spot}");

            // 1. Monthly Long Straddle (Next Month)
            // Logic: Buy CE & PE, Expiry = Monthly, Offset = 0 (Current Month) or 1 (Next Month) depending on date
            // For now, assuming "Next Month" roughly
            
            // 2. Weekly Short Straddle (Next Week)
            // Logic: Sell CE & PE, Expiry = Weekly, Offset = 1 (Next Week)

            // NOTE: In a real implementation we need accurate Expiry Date calculation service
            // For this implementation, we will simulate the order placement
            
            var leg1 = CreateLeg("CE", ActionType.Buy, "Monthly", 1);
            var leg2 = CreateLeg("PE", ActionType.Buy, "Monthly", 1);
            var leg3 = CreateLeg("CE", ActionType.Sell, "Weekly", 1);
            var leg4 = CreateLeg("PE", ActionType.Sell, "Weekly", 1);

            await Task.WhenAll(ExecuteLeg(leg1), ExecuteLeg(leg2), ExecuteLeg(leg3), ExecuteLeg(leg4));
        }

        private StrategyLeg CreateLeg(string type, ActionType action, string expiryType, int offset)
        {
             return new StrategyLeg 
             {
                 SymbolToken = $"{_config.Symbol} {expiryType} {type}", // Placeholder, will be resolved by Engine
                 OptionType = type == "CE" ? OptionType.Call : OptionType.Put,
                 Action = action,
                 ExpiryType = expiryType,
                 ExpiryOffset = offset,
                 TotalLots = _config.TotalLots,
                 ProductType = _config.ProductType,
                 Status = "PENDING",
                 Mode = StrikeSelectionMode.ATMPoint
             };
        }

        private async Task ExecuteLeg(StrategyLeg leg)
        {
            // Delegate component execution to Engine
            // Since Engine needs StrategyConfig, we pass ours
            string orderId = await _engine.ExecuteLeg(leg, new StrategyConfig { Id=_config.Id, Name=Name });
            if (orderId != "FAILED")
            {
                leg.Status = "OPEN";
                _activeLegs.Add(leg);
            }
        }

        private async Task ManageWeeklyRoll(TickerData ticker)
        {
            // 1. Close ALL current weekly legs
            var weeklyLegs = _activeLegs.Where(l => l.ExpiryType == "Weekly" && l.Status == "OPEN").ToList();
            foreach (var leg in weeklyLegs)
            {
                Console.WriteLine($"[Calendar] Rolling: Closing {leg.SymbolToken}");
                await _engine.ExecuteOrderAsync(new StrategyConfig { Id=_config.Id, Name=Name }, leg.SymbolToken, leg.Action == ActionType.Buy ? "SELL" : "BUY");
                leg.Status = "EXITED";
                leg.ExitTime = DateTime.Now;
                _activeLegs.Remove(leg);
            }

            // 2. Check Last Week
            if (IsLastWeekOfMonthlyCycle())
            {
                 Console.WriteLine("[Calendar] Last Week: Exiting Monthly.");
                 var monthlyLegs = _activeLegs.Where(l => l.ExpiryType == "Monthly" && l.Status == "OPEN").ToList();
                 foreach (var leg in monthlyLegs)
                 {
                     await _engine.ExecuteOrderAsync(new StrategyConfig { Id=_config.Id, Name=Name }, leg.SymbolToken, leg.Action == ActionType.Buy ? "SELL" : "BUY");
                     leg.Status = "EXITED";
                     leg.ExitTime = DateTime.Now;
                     _activeLegs.Remove(leg);
                 }
                 return;
            }

            // 3. Enter Next Weekly
            Console.WriteLine("[Calendar] Rolling: Entering Next Weekly...");
            var leg1 = CreateLeg("CE", ActionType.Sell, "Weekly", 1);
            var leg2 = CreateLeg("PE", ActionType.Sell, "Weekly", 1);
            
            await Task.WhenAll(ExecuteLeg(leg1), ExecuteLeg(leg2));
        }

        private bool IsLastWeekOfMonthlyCycle()
        {
            // Simple heuristic for Nifty (Last Thursday is Expiry)
            // If today is Tuesday, and Next Tuesday is in a different month, then this is the last Tuesday of the month?
            // User says "Monthly Exit: If the current Weekly Expiry is the same as the Monthly Long Straddle Expiry"
            
            // Let's assume Valid Logic:
            // Calculate Current Weekly Expiry Date
            // Calculate Current Monthly Expiry Date
            // If Weekly == Monthly, return true.
            
            DateTime today = DateTime.Today;
            DateTime nextWeeklyExpiry = GetNextExpiry(today, DayOfWeek.Tuesday); // Weekly Expiry is Tuesday per user
            DateTime monthlyExpiry = GetMonthlyExpiry(today);

            // If the upcoming weekly expiry IS the monthly expiry
            return nextWeeklyExpiry.Date == monthlyExpiry.Date;
        }

        private DateTime GetNextExpiry(DateTime fromDate, DayOfWeek day)
        {
             // Find next Tuesday
             int daysUntil = ((int)day - (int)fromDate.DayOfWeek + 7) % 7;
             if (daysUntil == 0) daysUntil = 7; // If today is Tuesday, next is today? No, user says "Every Tuesday", implies today if time matches?
             // Actually, if we are AT 15:24 on Tuesday, the "Current Weekly" is expiring TODAY (or has expired?).
             // User says "Expiry Day: Every Tuesday". So today IS Expiry.
             // So "Next Weekly" is Today + 7.
             return fromDate.AddDays(0); // Today is expiry
        }

        private DateTime GetMonthlyExpiry(DateTime fromDate)
        {
            // Last Tuesday of the month
            DateTime lastDayOfMonth = new DateTime(fromDate.Year, fromDate.Month, DateTime.DaysInMonth(fromDate.Year, fromDate.Month));
            while (lastDayOfMonth.DayOfWeek != DayOfWeek.Tuesday)
            {
                lastDayOfMonth = lastDayOfMonth.AddDays(-1);
            }
            return lastDayOfMonth;
        }

        private async Task ManageRisk(TickerData ticker)
        {
            if (_isReversalActive) 
            {
                // Manage Trailing SL for Buy Legs
                foreach(var leg in _activeLegs.Where(l => l.Action == ActionType.Buy && l.Status == "OPEN" && l.ExpiryType == "Weekly"))
                {
                    double ltp = GetLtp(leg, ticker);
                    // Update Trailing Logic here...
                    if (ltp <= leg.StopLossPrice)
                    {
                         Console.WriteLine($"[Calendar] Reversal Trailing SL Hit for {leg.SymbolToken}");
                         await _engine.ExecuteOrderAsync(new StrategyConfig { Id=_config.Id, Name=Name }, leg.SymbolToken, "SELL");
                         leg.Status = "EXITED";
                         leg.ExitTime = DateTime.Now;
                    }
                }
                return;
            }

            // Monitor Short Straddle Premium
            var shortLegs = _activeLegs.Where(l => l.ExpiryType == "Weekly" && l.Action == ActionType.Sell && l.Status == "OPEN").ToList();
            if (shortLegs.Count != 2) return; // Need both legs for straddle check

            double combinedPremium = 0;
            foreach(var leg in shortLegs) combinedPremium += GetLtp(leg, ticker);

            // Check Combined SL
            // Simplified: If Combined Premium > Entry Premium + SL
            double entryPremium = shortLegs.Sum(l => l.EntryPrice);
            if (combinedPremium > entryPremium + _config.ShortStraddleCombinedSL)
            {
                Console.WriteLine($"[Calendar] Short Straddle SL Hit! Initiating Reversal...");
                
                // Identify Offending Leg (The one that gained the most value)
                var lossLeg = shortLegs.OrderByDescending(l => GetLtp(l, ticker) - l.EntryPrice).First();
                
                // REVERSAL LOGIC: Buy the Losing Leg (Stop and Reverse)
                // 1. Close the Short
                await _engine.ExecuteOrderAsync(new StrategyConfig { Id=_config.Id, Name=Name }, lossLeg.SymbolToken, "BUY");
                lossLeg.Status = "EXITED";
                _activeLegs.Remove(lossLeg);

                // 2. Open Long (Reversal)
                var revLeg = CreateLeg(lossLeg.OptionType == OptionType.Call ? "CE" : "PE", ActionType.Buy, "Weekly", 1); // Buy same expiry
                revLeg.StopLossPrice = GetLtp(lossLeg, ticker) * (1 - (_config.BuyStopLossPercent/100.0)); // Initial SL
                
                await ExecuteLeg(revLeg);
                _isReversalActive = true;
            }
        }
        
        private double GetLtp(StrategyLeg leg, TickerData ticker)
        {
            // In real scenario, we need Option Chain data or specific token subscription
            // For now, we return a mock value or 0 if not available
            return 100.0;
        }

        private double GetLtp(TickerData ticker, string symbol)
        {
             if ((symbol == "NIFTY" || symbol == "NIFTY 50") && ticker.Nifty != null) return ticker.Nifty.Ltp;
             if ((symbol == "BANKNIFTY" || symbol == "NIFTY BANK") && ticker.BankNifty != null) return ticker.BankNifty.Ltp;
             return 0;
        }
    }
}
