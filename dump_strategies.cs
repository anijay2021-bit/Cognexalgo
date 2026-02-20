using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Cognexalgo.Core.Data;
using Cognexalgo.Core.Data.Entities;
using Microsoft.Extensions.Configuration;

class Program
{
    static async Task Main(string[] args)
    {
        var conn = "postgresql://postgres.eoujveqntxidrkcljylv:P79M7C5r5pS8kC!@aws-0-ap-south-1.pooler.supabase.com:5432/postgres";
        
        var optionsBuilder = new DbContextOptionsBuilder<MetadataContext>();
        optionsBuilder.UseNpgsql(conn);

        using (var context = new MetadataContext(optionsBuilder.Options))
        {
            var strategies = await context.HybridStrategies.ToListAsync();
            Console.WriteLine("--- STRATEGIES DUMP ---");
            foreach (var s in strategies)
            {
                Console.WriteLine($"ID: {s.Id}, Name: {s.Name}, Active: {s.IsActive}");
                Console.WriteLine($"Config: {s.ConfigJson}");
                Console.WriteLine("-------------------------");
            }
        }
    }
}
