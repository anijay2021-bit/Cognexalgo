using System;
using System.Collections.Generic;
using System.Linq;
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
        private HybridStrategySettings _settings;

        public HybridStraddleStrategy(TradingEngine engine) : base(engine, "Hybrid Straddle") 
        { 
        }

        public void Initialize(string jsonParams)
        {
            if (string.IsNullOrEmpty(jsonParams)) return;
            try 
            {
                _settings = Newtonsoft.Json.JsonConvert.DeserializeObject<HybridStrategySettings>(jsonParams);
                if (_settings != null)
                {
                    if (TimeSpan.TryParse(_settings.EntryTime, out var entry)) EntryTime = entry;
                    if (TimeSpan.TryParse(_settings.ExitTime, out var exit)) ExitTime = exit;
                    
                    Symbol = _settings.Symbol ?? "BANKNIFTY";
                    
                    if (_settings.Legs != null)
                    {
                        Legs = _settings.Legs;
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

            // --- Dynamic Engine Settings (Hybrid v2.0) ---
            public StrikeSelectionType SelectionType { get; set; } = StrikeSelectionType.ATM;
            public double TargetValue { get; set; } = 0; // Usage: Premium Amount or Delta Value
            
            public AdjustmentTriggerType AdjustmentTrigger { get; set; } = AdjustmentTriggerType.None;
            public double AdjustmentThreshold { get; set; } = 0; // Usage: 0.3 = 30% diff
            
            public AdjustmentAction Action { get; set; } = AdjustmentAction.ShiftWhole;
        }

        public enum StrikeSelectionType { ATM, PremiumMatch, Delta }
        public enum AdjustmentTriggerType { None, PremiumDifference, LegStopLoss, CombinedMTM }
        public enum AdjustmentAction { ShiftWhole, ExitLoser, AddCover }

        public override async Task OnTickAsync(TickerData ticker)
        {
            if (!IsActive) return;

            var now = DateTime.Now.TimeOfDay;
            
            // 1. Check Entry Time for PENDING legs
            if (now >= EntryTime && now < ExitTime)
            {
                foreach (var leg in Legs.Where(l => l.Status == "PENDING"))
                {
                    // Calculate Strike based on Selection Mode
                    if (leg.CalculatedStrike == 0)
                    {
                        leg.CalculatedStrike = await SelectStrike(leg, ticker);
                    }

                    // Execute Leg
                    // Note: ExecuteLeg needs to support passing specific Strike if calculated
                    // For now, let's assume ExecuteLeg handles the order placement
                    string result = await _engine.ExecuteLeg(leg, new StrategyConfig { Id=0, Name="Hybrid" }); 
                    
                    if (result != "FAILED" && result != "SKIPPED" && result != "PENDING" && result != "ERROR")
                    {
                        leg.Status = "OPEN";
                        leg.OrderId = result;
                        leg.EntryTime = DateTime.Now;
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

            // 3. Dynamic Adjustments (Hybrid v2.0)
            if (_isPositionOpen && _settings != null && _settings.AdjustmentTrigger != AdjustmentTriggerType.None)
            {
                await MonitorAdjustments(ticker);
            }

            // 4. Global Exit Time
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

        private async Task<int> SelectStrike(StrategyLeg leg, TickerData ticker)
        {
            double underlyingLtp = (Symbol == "NIFTY") ? ticker.Nifty.Ltp : ticker.BankNifty.Ltp;
            
            if (_settings.SelectionType == StrikeSelectionType.PremiumMatch)
            {
                // Logic: Fetch Option Chain -> Find Strike closely matching TargetValue
                var chain = await _engine.DataService.BuildOptionChainAsync(Symbol, leg.ExpiryType ?? "WEEKLY");
                if (chain != null)
                {
                    var bestMatch = chain
                        .Where(x => x.IsCall == (leg.OptionType == OptionType.Call))
                        .OrderBy(x => Math.Abs(x.LTP - _settings.TargetValue))
                        .FirstOrDefault();
                        
                    if (bestMatch != null) return bestMatch.Strike;
                }
            }
            else if (_settings.SelectionType == StrikeSelectionType.Delta)
            {
                 // Logic: Use Greeks to find Delta match
                 // Simplified for now: Fallback to ATM
            }

            // Default: ATM Rounding
            int step = (Symbol == "NIFTY") ? 50 : 100;
            return (int)(Math.Round(underlyingLtp / step) * step);
        }

        private async Task MonitorAdjustments(TickerData ticker)
        {
            // Only applicable for Straddles (2 Legs Open)
            var openLegs = Legs.Where(l => l.Status == "OPEN").ToList();
            if (openLegs.Count < 2) return;

            if (_settings.AdjustmentTrigger == AdjustmentTriggerType.PremiumDifference)
            {
                var ceLeg = openLegs.FirstOrDefault(l => l.OptionType == OptionType.Call);
                var peLeg = openLegs.FirstOrDefault(l => l.OptionType == OptionType.Put);

                if (ceLeg != null && peLeg != null)
                {
                    double ratio = ceLeg.Ltp / peLeg.Ltp;
                    double threshold = 1 + _settings.AdjustmentThreshold; // e.g. 1.3 for 30%
                    
                    // Logic: If one premium is > 30% of other
                    bool trigger = ratio > threshold || ratio < (1/threshold);
                    
                    if (trigger)
                    {
                        Console.WriteLine($"[Hybrid] Premium De-coupling detected! Ratio: {ratio:F2}");
                        await ExecuteAdjustment(ceLeg, peLeg);
                    }
                }
            }
        }

        private async Task ExecuteAdjustment(StrategyLeg leg1, StrategyLeg leg2)
        {
            if (_settings.Action == AdjustmentAction.ShiftWhole)
            {
                // Close Both
                await SquareOffLeg(leg1, "AdjustmentShift");
                await SquareOffLeg(leg2, "AdjustmentShift");
                
                // Re-Enter Both (Reset to PENDING)
                leg1.Status = "PENDING";
                leg1.CalculatedStrike = 0; // Force Re-Select
                leg1.EntryTime = null;
                
                leg2.Status = "PENDING";
                leg2.CalculatedStrike = 0;
                leg2.EntryTime = null;
                
                Console.WriteLine("[Hybrid] Straddle Shift Executed. Legs reset to PENDING.");
            }
            else if (_settings.Action == AdjustmentAction.ExitLoser)
            {
                 // Identify Loser (Higher PnL loss) -> Only Close that one
                 // Simplified: Close the one with higher LTP if Short Straddle
                 var loser = (leg1.Ltp > leg2.Ltp) ? leg1 : leg2;
                 await SquareOffLeg(loser, "AdjustmentCutLoser");
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

            // STRADDLE SHIFT LOGIC (Legacy Support for SL-based Shift)
            if (reason == "StopLoss" && !string.IsNullOrEmpty(leg.StraddlePairId) && leg.CurrentReEntry < leg.MaxReEntry)
            {
                // ... Existing Logic ...
            }
        }

        private int GetAtmStrike(double ltp, string symbol)
        {
             int step = (symbol == "NIFTY") ? 50 : 100;
             return (int)(Math.Round(ltp / step) * step);
        }
    }
}
