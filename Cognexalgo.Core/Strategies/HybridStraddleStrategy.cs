using System;
using System.Threading.Tasks;
using Cognexalgo.Core.Models;

namespace Cognexalgo.Core.Strategies
{
    public class HybridStraddleStrategy : StrategyBase
    {
        public string Symbol { get; set; } = "BANKNIFTY";
        public TimeSpan EntryTime { get; set; } = new TimeSpan(9, 30, 0);
        public TimeSpan ExitTime { get; set; } = new TimeSpan(15, 15, 0);
        
        private bool _isPositionOpen = false;

        public HybridStraddleStrategy(TradingEngine engine) : base(engine, "Hybrid Straddle") 
        { 
        }

        public void Initialize(string jsonParams)
        {
            if (string.IsNullOrEmpty(jsonParams)) return;
            try 
            {
                var settings = Newtonsoft.Json.JsonConvert.DeserializeObject<HybridStrategySettings>(jsonParams);
                if (settings != null)
                {
                    if (TimeSpan.TryParse(settings.EntryTime, out var entry)) EntryTime = entry;
                    if (TimeSpan.TryParse(settings.ExitTime, out var exit)) ExitTime = exit;
                    
                    Symbol = settings.Symbol ?? "BANKNIFTY";
                    
                    if (settings.Legs != null)
                    {
                        Legs = settings.Legs;
                        foreach(var leg in Legs) leg.Status = "PENDING";
                    }
                }
            }
            catch (Exception ex) 
            { 
                Console.WriteLine($"[Hybrid] Init Error: {ex.Message}");
            }
        }

        public List<StrategyLeg> Legs { get; set; } = new List<StrategyLeg>();
        
        public class HybridStrategySettings 
        {
            public string Name { get; set; }
            public string EntryTime { get; set; }
            public string ExitTime { get; set; }
            public List<StrategyLeg> Legs { get; set; } // Dynamic Legs
            
            public int LotSize { get; set; } = 1;
            public string Symbol { get; set; } = "BANKNIFTY";
        }

        public override async Task OnTickAsync(TickerData ticker)
        {
            if (!IsActive) return;

            var now = DateTime.Now.TimeOfDay;
            
            // 1. Check Entry Time for PENDING legs
            if (now >= EntryTime && now < ExitTime)
            {
                foreach (var leg in Legs.Where(l => l.Status == "PENDING"))
                {
                    // Check if leg has specific start time, else use Strategy EntryTime
                    // Execute Leg
                    string result = await _engine.ExecuteLeg(leg, new StrategyConfig { Id=0, Name="Hybrid" }); // Need to pass actual config
                    
                    if (result != "FAILED" && result != "SKIPPED" && result != "PENDING" && result != "ERROR")
                    {
                        // Success
                        leg.Status = "OPEN";
                        leg.OrderId = result;
                        leg.EntryTime = DateTime.Now;
                        // leg.EntryPrice is set inside ExecuteLeg (we need to ensure this)
                        // For now assuming we fetch order details later or Engine executes it market
                        _isPositionOpen = true;
                    }
                }
            }

            // 2. Monitor OPEN legs (SL/Target/LTP)
            foreach (var leg in Legs.Where(l => l.Status == "OPEN"))
            {
                // Update LTP from Ticker
                double currentLtp = GetLtp(leg, ticker);
                leg.Ltp = currentLtp;

                // Check Stop Loss
                if (leg.StopLossPrice > 0 && 
                   ((leg.Action == ActionType.Buy && currentLtp <= leg.StopLossPrice) ||
                    (leg.Action == ActionType.Sell && currentLtp >= leg.StopLossPrice)))
                {
                    await SquareOffLeg(leg, "StopLoss");
                }
                
                // Check Target
                else if (leg.TargetPrice > 0 &&
                   ((leg.Action == ActionType.Buy && currentLtp >= leg.TargetPrice) ||
                    (leg.Action == ActionType.Sell && currentLtp <= leg.TargetPrice)))
                {
                    await SquareOffLeg(leg, "Target");
                }
            }

            // 3. Global Exit Time
            if (now >= ExitTime && Legs.Any(l => l.Status == "OPEN"))
            {
                Console.WriteLine($"[Hybrid] Global Exit Time {ExitTime} reached.");
                foreach(var leg in Legs.Where(l => l.Status == "OPEN"))
                {
                     await SquareOffLeg(leg, "TimeExit");
                }
                _isPositionOpen = false;
                IsActive = false; // Stop strategy for the day
            }
        }

        private double GetLtp(StrategyLeg leg, TickerData ticker)
        {
            // 1. Get Current Underlying Price
            double currentIndexLtp = (Symbol == "NIFTY") ? ticker.Nifty.Ltp : ticker.BankNifty.Ltp;
            
            // 2. Calculate Change in Underlying
            double change = currentIndexLtp - leg.EntryIndexLtp;
            
            // 3. Approximate Option Movement (Delta ~ 0.5 for ATM)
            // Call: +Change * 0.5
            // Put:  -Change * 0.5
            double delta = 0.5;
            double optionChange = (leg.OptionType == OptionType.Call) ? (change * delta) : (-change * delta);
            
            // 4. Return Simulated Option Price
            // Ensure strictly positive
            return Math.Max(0.05, leg.EntryPrice + optionChange);
        }

        private async Task SquareOffLeg(StrategyLeg leg, string reason)
        {
            Console.WriteLine($"[Hybrid] Squaring off Leg {leg.SymbolToken} due to {reason}");
            
            // Create Opposite Order
            string action = leg.Action == ActionType.Buy ? "SELL" : "BUY";
            await _engine.ExecuteOrderAsync(new StrategyConfig { Id=0, Name="Hybrid" }, leg.SymbolToken, action);
            
            leg.Status = "EXITED";
            leg.ExitTime = DateTime.Now;

            // STRADDLE SHIFT LOGIC
            if (reason == "StopLoss" && !string.IsNullOrEmpty(leg.StraddlePairId) && leg.CurrentReEntry < leg.MaxReEntry)
            {
                // 1. Find Partner Leg
                var partner = Legs.FirstOrDefault(l => l.StraddlePairId == leg.StraddlePairId && l != leg && l.Status == "OPEN");
                
                if (partner != null)
                {
                    Console.WriteLine($"[Hybrid] Straddle Shift Triggered! Closing Partner Leg {partner.SymbolToken}");
                    // Close Partner
                    string partnerAction = partner.Action == ActionType.Buy ? "SELL" : "BUY";
                     await _engine.ExecuteOrderAsync(new StrategyConfig { Id=0, Name="Hybrid" }, partner.SymbolToken, partnerAction);
                    partner.Status = "EXITED";
                    partner.ExitTime = DateTime.Now;
                    
                    // 2. Re-Enter Both (New Straddle)
                    Console.WriteLine($"[Hybrid] Adjusting Straddle... Re-Entry {leg.CurrentReEntry + 1}/{leg.MaxReEntry}");
                    
                    // Reset Both
                    leg.Status = "PENDING";
                    leg.CurrentReEntry++;
                    leg.EntryTime = null; // Forces new ATM calculation based on current time
                    leg.CalculatedStrike = 0; // Clear old strike
                    
                    partner.Status = "PENDING";
                    partner.CurrentReEntry++; // Increment partner too to keep in sync
                    partner.EntryTime = null;
                    partner.CalculatedStrike = 0;
                    
                    // The main loop will pick them up as PENDING in the next tick
                    _isPositionOpen = false; // Temporarily false until re-entry
                }
            }
        }

        private int GetAtmStrike(double ltp, string symbol)
        {
             int step = (symbol == "NIFTY") ? 50 : 100;
             return (int)(Math.Round(ltp / step) * step);
        }
    }
}
