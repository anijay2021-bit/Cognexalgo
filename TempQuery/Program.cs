using System;
using System.Linq;
using Cognexalgo.Core.Data;

namespace TempQuery
{
    class Program
    {
        static void Main(string[] args)
        {
            try 
            {
                using var db = new AlgoDbContext();
                var strategy = db.HybridStrategies.FirstOrDefault(s => s.Name == "21ema ce");
                if (strategy != null)
                {
                    Console.WriteLine("------------------------------------------------------------------");
                    Console.WriteLine($"Strategy: {strategy.Name}");
                    Console.WriteLine($"Active: {strategy.IsActive}");
                    Console.WriteLine($"ConfigJson:");
                    Console.WriteLine(strategy.ConfigJson);
                    Console.WriteLine("------------------------------------------------------------------");
                }
                else 
                {
                    Console.WriteLine("Strategy '21ema ce' not found in database.");
                }
            } 
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }
    }
}
