using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Cognexalgo.Core.Data;
using Cognexalgo.Core.Repositories;
using Cognexalgo.Core.Models;

namespace Cognexalgo.Tests
{
    public class DatabasePersistenceTest
    {
        private IConfiguration _configuration;
        private DbContextOptions<AlgoDbContext> _options;

        public DatabasePersistenceTest()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            _configuration = builder.Build();

            var connectionString = _configuration.GetConnectionString("AlgoDatabase");
            _options = new DbContextOptionsBuilder<AlgoDbContext>()
                .UseNpgsql(connectionString)
                .Options;
        }

        [Fact]
        public async Task CanConnectAndSaveStrategy()
        {
            try
            {
                // 1. Setup Context & Repository
                using (var context = new AlgoDbContext(_options))
                {
                    // Ensure Database Created
                    System.Console.WriteLine("Attempting to connect to database...");
                    await context.Database.EnsureCreatedAsync();
                    System.Console.WriteLine("Database connection successful.");

                    var repository = new StrategyRepository(context);

                    // 2. Create 'Nifty 25900' Strategy
                    var strategy = new HybridStrategyConfig
                    {
                        Name = "Nifty 25900 Hybrid",
                        IsActive = true,
                        ProductType = "MIS",
                        ExpiryType = "Weekly",
                        Legs = new List<StrategyLeg>
                        {
                            new StrategyLeg
                            {
                                Index = "NIFTY",
                                OptionType = OptionType.Call,
                                TargetPremium = 50.0,
                                Action = ActionType.Buy,
                                TotalLots = 1
                            }
                        }
                    };

                    // 3. Save Strategy
                    System.Console.WriteLine("Saving strategy...");
                    int id = await repository.SaveHybridStrategyAsync(strategy, "TestUser");
                    Assert.True(id > 0, "Strategy ID should be greater than 0");

                    // 4. Retrieve Strategy
                    var savedStrategy = await repository.GetHybridStrategyAsync(id);
                    Assert.NotNull(savedStrategy);
                    Assert.Equal("Nifty 25900 Hybrid", savedStrategy.Name);
                    Assert.Single(savedStrategy.Legs);
                    Assert.Equal("NIFTY", savedStrategy.Legs[0].Index);

                    Console.WriteLine($"Successfully saved strategy ID: {id}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("--------------------------------------------------");
                Console.WriteLine($"CONNECTION ERROR: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"INNER EXCEPTION: {ex.InnerException.Message}");
                }
                Console.WriteLine("--------------------------------------------------");
                throw; // Fail the test
            }
        }
    }
}
