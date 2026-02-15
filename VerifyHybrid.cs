using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// FLATTENED STRUCTURE - NO NAMESPACES

public enum ActionType { Buy, Sell }

public class StrategyLeg {
    public string SymbolToken { get; set; }
    public ActionType Action { get; set; }
    public string Status { get; set; }
    public string StraddlePairId { get; set; }
    public int MaxReEntry { get; set; }
    public int CurrentReEntry { get; set; }
    public double StopLossPrice { get; set; }
    public double EntryPrice { get; set; }
    public double Ltp { get; set; }
    public DateTime? EntryTime { get; set; }
    public DateTime? ExitTime { get; set; }
    public int CalculatedStrike { get; set; }
}

public class Program 
{
    public static void Main()
    {
        Console.WriteLine("Verifying Hybrid Strategy Logic...");
        var test = new HybridLogicTest();
        test.RunSimulation().Wait();
    }
}

public class HybridLogicTest 
{
    public async Task RunSimulation()
    {
        // 1. Setup Strategy with 2 Legs (Straddle Pair)
        var leg1 = new StrategyLeg { SymbolToken="CE", Action=ActionType.Sell, Status="PENDING", StraddlePairId="S1", MaxReEntry=1, StopLossPrice=150, EntryPrice=100 };
        var leg2 = new StrategyLeg { SymbolToken="PE", Action=ActionType.Sell, Status="PENDING", StraddlePairId="S1", MaxReEntry=1, StopLossPrice=150, EntryPrice=100 };
        
        var legs = new List<StrategyLeg> { leg1, leg2 };
        
        Console.WriteLine("--- SIMULATION START ---");
        
        // 2. Simulate Entry
        Console.WriteLine("\n[1] Simulating Entry...");
        leg1.Status = "OPEN"; leg1.EntryPrice = 100; leg1.Ltp = 100;
        leg2.Status = "OPEN"; leg2.EntryPrice = 100; leg2.Ltp = 100;
        Console.WriteLine($"Leg 1 (CE) Status: {leg1.Status}, Price: {leg1.Ltp}");
        Console.WriteLine($"Leg 2 (PE) Status: {leg2.Status}, Price: {leg2.Ltp}");

        // 3. Simulate Market Move (CE spikes to 160 -> Hits SL 150)
        Console.WriteLine("\n[2] Simulating Market Spike (CE -> 160)...");
        leg1.Ltp = 160; 
        
        if (leg1.Ltp >= leg1.StopLossPrice)
        {
            Console.WriteLine($"!! Leg 1 Hit SL ({leg1.Ltp} >= {leg1.StopLossPrice}) !!");
            await SquareOffLeg(leg1, "StopLoss", legs);
        }
        
        // 4. Verify Results
        Console.WriteLine("\n[3] Verifying States after Adjustments...");
        Console.WriteLine($"Leg 1 Status: {leg1.Status} (Expected: PENDING caused by ReEntry)");
        Console.WriteLine($"Leg 2 Status: {leg2.Status} (Expected: PENDING caused by Shift)");
        Console.WriteLine($"Leg 1 ReEntryCount: {leg1.CurrentReEntry} (Expected: 1)");
    }

    private async Task SquareOffLeg(StrategyLeg leg, string reason, List<StrategyLeg> legs)
    {
        Console.WriteLine($"Squaring off {leg.SymbolToken} due to {reason}");
        leg.Status = "EXITED";

        if (reason == "StopLoss" && !string.IsNullOrEmpty(leg.StraddlePairId) && leg.CurrentReEntry < leg.MaxReEntry)
        {
             var partner = legs.Find(l => l.StraddlePairId == leg.StraddlePairId && l != leg && l.Status == "OPEN");
             if (partner != null)
             {
                 Console.WriteLine($"[Logic] Straddle Shift Triggered! Closing Partner {partner.SymbolToken}");
                 partner.Status = "EXITED";
                 
                 Console.WriteLine($"[Logic] Re-Entering Both Legs (Shift)...");
                 leg.Status = "PENDING";
                 leg.CurrentReEntry++;
                 
                 partner.Status = "PENDING";
                 partner.CurrentReEntry++;
             }
        }
    }
}
