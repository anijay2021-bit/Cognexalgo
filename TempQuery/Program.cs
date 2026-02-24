using System;
using System.IO;
using System.Threading.Tasks;
using Npgsql;

namespace TempQuery
{
    class Program
    {
        static async Task Main(string[] args)
        {
            string connStr = "Host=aws-1-ap-southeast-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.dcsjwozwltcixdlgzalr;Password=3GTeWMvIwGBHMQXd;SSL Mode=Require;Trust Server Certificate=true;Command Timeout=60;";
            try 
            {
                using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync();
                
                using var cmd = new NpgsqlCommand("SELECT \"Name\", \"ConfigJson\" FROM hybrid_strategies WHERE \"Id\" = 26", conn);
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    File.WriteAllText("lid26_config.json", reader.GetString(1));
                    Console.WriteLine("Saved LID 26 config to lid26_config.json");
                }
                else
                {
                    Console.WriteLine("Strategy ID 26 not found.");
                }
            } 
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }
    }
}
