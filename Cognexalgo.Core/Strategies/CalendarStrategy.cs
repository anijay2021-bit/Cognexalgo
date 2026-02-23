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

            // [MARGIN OPTIMIZATION] Execute Buys (Monthly Long) first to get margin benefit
            await Task.WhenAll(ExecuteLeg(leg1), ExecuteLeg(leg2));
            
            // Execute Sells (Weekly Short) only after Buys are confirmed
            await Task.WhenAll(ExecuteLeg(leg3), ExecuteLeg(leg4));
        }

        private StrategyLeg CreateLeg(string type, ActionType action, string expiryType, int offset)
        {
             return new StrategyLeg 
             {
                 Index = _config.Symbol, // FIX 1: Set Index so ExecuteLeg can resolve spot/chain
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

        /// <summary>
        /// FIX 4: Uses authoritative TokenService expiry data from Scrip Master
        /// instead of broken manual day-of-week calculations.
        /// Returns true if the next weekly expiry == the monthly expiry (end of calendar cycle).
        /// </summary>
        private bool IsLastWeekOfMonthlyCycle()
        {
            try
            {
                DateTime weeklyExpiry = _engine.TokenService.GetNextExpiry(_config.Symbol, "Weekly");
                DateTime monthlyExpiry = _engine.TokenService.GetNextExpiry(_config.Symbol, "Monthly");

                Console.WriteLine($"[Calendar] Expiry Check: Weekly={weeklyExpiry:dd-MMM-yyyy}, Monthly={monthlyExpiry:dd-MMM-yyyy}");

                // If the upcoming weekly expiry IS the monthly expiry, this is the last week
                return weeklyExpiry.Date == monthlyExpiry.Date;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Calendar] ERROR in IsLastWeekOfMonthlyCycle: {ex.Message}");
                return false; // Safe default: don't exit monthly positions on error
            }
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
        
        /// <summary>
        /// FIX 5: Uses the leg's live LTP (set by ExecuteLeg in TradingEngine)
        /// instead of a hardcoded mock value.
        /// </summary>
        private double GetLtp(StrategyLeg leg, TickerData ticker)
        {
            // Use the LTP that was populated by the engine when the leg was executed
            if (leg.Ltp > 0) return leg.Ltp;
            // Fallback to entry price if LTP hasn't been updated yet
            if (leg.EntryPrice > 0) return leg.EntryPrice;
            return 0;
        }

        private double GetLtp(TickerData ticker, string symbol)
        {
             if ((symbol == "NIFTY" || symbol == "NIFTY 50") && ticker.Nifty != null) return ticker.Nifty.Ltp;
             if ((symbol == "BANKNIFTY" || symbol == "NIFTY BANK") && ticker.BankNifty != null) return ticker.BankNifty.Ltp;
             return 0;
        }
    }
}
