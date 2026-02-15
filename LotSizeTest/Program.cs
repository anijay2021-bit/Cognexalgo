using System;
using System.Threading.Tasks;
using Cognexalgo.Core.Services;

namespace Cognexalgo.Test
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Testing Automatic Lot Size Retrieval ===\n");

            // 1. Create TokenService
            var tokenService = new TokenService();

            // 2. Load Scrip Master
            Console.WriteLine("Loading Scrip Master from Angel One...");
            await tokenService.LoadMasterAsync();
            Console.WriteLine("✓ Scrip Master loaded\n");

            // 3. Test lot size retrieval for common symbols
            string[] testSymbols = new[] 
            { 
                "NIFTY", 
                "BANKNIFTY",
                "FINNIFTY",
                "MIDCPNIFTY"
            };

            Console.WriteLine("Testing Lot Size Retrieval:\n");
            foreach (var symbol in testSymbols)
            {
                var lotSize = tokenService.GetLotSize(symbol);
                var (token, lotSizeFromInfo) = tokenService.GetInstrumentInfo(symbol);
                
                Console.WriteLine($"Symbol: {symbol}");
                Console.WriteLine($"  Lot Size: {lotSize}");
                Console.WriteLine($"  Token: {token ?? "NOT FOUND"}");
                Console.WriteLine($"  GetInstrumentInfo LotSize: {lotSizeFromInfo}");
                Console.WriteLine();
            }

            // 4. Test quantity calculation
            Console.WriteLine("=== Testing Quantity Calculation ===\n");
            
            int userLots = 2;
            var niftyLotSize = tokenService.GetLotSize("NIFTY");
            var bankniftyLotSize = tokenService.GetLotSize("BANKNIFTY");
            
            Console.WriteLine($"User wants to trade: {userLots} lots");
            Console.WriteLine($"NIFTY: {userLots} lots × {niftyLotSize} lot size = {userLots * niftyLotSize} contracts");
            Console.WriteLine($"BANKNIFTY: {userLots} lots × {bankniftyLotSize} lot size = {userLots * bankniftyLotSize} contracts");

            Console.WriteLine("\n✓ Test completed successfully!");
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}
