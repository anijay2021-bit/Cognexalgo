using System;
using Microsoft.Data.Sqlite;

class Program
{
    static void Main(string[] args)
    {
        string dbPath = @"c:\Users\anijay\.gemini\antigravity\scratch\COGNEX\Cognexalgo.UI\bin\Debug\net8.0-windows\cognex.db";
        string connString = $"Data Source={dbPath}";
        using (var conn = new SqliteConnection(dbPath)) // Simplified path
        {
             try {
                conn.ConnectionString = connString;
                conn.Open();
                using (var cmd = new SqliteCommand("SELECT * FROM Orders ORDER BY Timestamp DESC LIMIT 5;", conn))
                using (var reader = cmd.ExecuteReader())
                {
                    Console.WriteLine("--- DB AUDIT: LATEST ORDERS ---");
                    int count = 0;
                    while (reader.Read())
                    {
                        Console.WriteLine($"Order: {reader["OrderId"]}, Symbol: {reader["Symbol"]}, Status: {reader["Status"]}");
                        count++;
                    }
                    if (count == 0) Console.WriteLine("No orders found in local DB.");
                }
             } catch (Exception ex) {
                 Console.WriteLine($"DB Error: {ex.Message}");
             }
        }
    }
}
