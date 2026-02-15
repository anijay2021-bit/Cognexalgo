using System;
using Npgsql;
using System.Threading.Tasks;

namespace Cognexalgo.DbSetup
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var connectionString = "Host=aws-1-ap-southeast-1.pooler.supabase.com;Port=6543;Database=postgres;Username=postgres.dcsjwozwltcixdlgzalr;Password=3GTeWMvIwGBHMQXd;SSL Mode=Require;Trust Server Certificate=true;Command Timeout=300;Pooling=true;MinPoolSize=1;MaxPoolSize=20";

            Console.WriteLine("Connecting to Database...");
            
            try 
            {
                using (var conn = new NpgsqlConnection(connectionString))
                {
                    await conn.OpenAsync();
                    Console.WriteLine("Connected!");

                    var createTableSql = @"
                        CREATE TABLE IF NOT EXISTS hybrid_strategies (
                            ""Id"" SERIAL PRIMARY KEY,
                            ""Name"" TEXT NOT NULL,
                            ""ConfigJson"" TEXT,
                            ""IsActive"" BOOLEAN NOT NULL DEFAULT TRUE,
                            ""Version"" INTEGER NOT NULL DEFAULT 1,
                            ""CreatedAt"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
                            ""CreatedBy"" TEXT,
                            ""LastModified"" TIMESTAMP WITH TIME ZONE,
                            ""LastModifiedBy"" TEXT
                        );
                        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_hybrid_strategies_Name"" ON hybrid_strategies (""Name"");
                    ";

                    Console.WriteLine("Creating table 'hybrid_strategies'...");
                    using (var cmd = new NpgsqlCommand(createTableSql, conn))
                    {
                        cmd.CommandTimeout = 300; // 5 minutes
                        await cmd.ExecuteNonQueryAsync();
                    }
                    Console.WriteLine("Table created/verified successfully!");

                    // Insert Test Data
                    var insertSql = @"
                        INSERT INTO hybrid_strategies (""Name"", ""ConfigJson"", ""IsActive"", ""Version"", ""CreatedAt"", ""CreatedBy"")
                        VALUES ('Nifty 25900 Hybrid', '{ ""Name"": ""Nifty 25900 Hybrid"", ""Legs"": [] }', true, 1, NOW(), 'DbSetup Tool')
                        ON CONFLICT (""Name"") DO NOTHING;
                    ";

                    Console.WriteLine("Inserting test strategy...");
                    using (var cmd = new NpgsqlCommand(insertSql, conn))
                    {
                        cmd.CommandTimeout = 300;
                        var rows = await cmd.ExecuteNonQueryAsync();
                        Console.WriteLine($"Rows inserted: {rows}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}
