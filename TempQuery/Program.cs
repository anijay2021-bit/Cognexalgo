using System;
using Npgsql;

class Program
{
    static void Main(string[] args)
    {
        string connString = "Host=aws-1-ap-southeast-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.dcsjwozwltcixdlgzalr;Password=3GTeWMvIwGBHMQXd;SSL Mode=Require;Trust Server Certificate=true;Command Timeout=60;Pooling=false;Include Error Detail=true";
        using (var conn = new NpgsqlConnection(connString))
        {
            conn.Open();
            using (var cmd = new NpgsqlCommand("SELECT \"ConfigJson\" FROM hybrid_strategies WHERE \"Name\" = 'dynamic 21 pe';", conn))
            using (var reader = cmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    Console.WriteLine("START_JSON");
                    Console.WriteLine(reader.GetString(0));
                    Console.WriteLine("END_JSON");
                }
                else
                {
                    Console.WriteLine("Strategy not found.");
                }
            }
        }
    }
}
