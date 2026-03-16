using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace Cognexalgo.Core.Database
{
    public class DatabaseService
    {
        private readonly string _connectionString;
        private readonly string _dbPath;

        public DatabaseService()
        {
            // Store DB in the App Directory
            _dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cognex.db");
            _connectionString = $"Data Source={_dbPath}";
            InitializeDatabase();
        }

        public SqliteConnection GetConnection()
        {
            return new SqliteConnection(_connectionString);
        }

        private void InitializeDatabase()
        {
            using (var connection = GetConnection())
            {
                connection.Open();

                // Migration: add Token column to Orders if it doesn't exist yet
                try
                {
                    var migrate = connection.CreateCommand();
                    migrate.CommandText = "ALTER TABLE Orders ADD COLUMN Token TEXT DEFAULT ''";
                    migrate.ExecuteNonQuery();
                    Console.WriteLine("[DB] Added Token column to Orders table.");
                }
                catch
                {
                    // Column already exists — ignore
                }

                var command = connection.CreateCommand();
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Strategies (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        Symbol TEXT NOT NULL,
                        ProductType TEXT DEFAULT 'MIS',
                        StrategyType TEXT NOT NULL, 
                        Parameters JSON,
                        IsActive INTEGER DEFAULT 0
                    );

                    CREATE TABLE IF NOT EXISTS Orders (
                        OrderId TEXT PRIMARY KEY,
                        StrategyId INTEGER,
                        Symbol TEXT NOT NULL,
                        TransactionType TEXT NOT NULL,
                        Qty REAL NOT NULL,
                        Price REAL NOT NULL,
                        Status TEXT NOT NULL,
                        Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
                        StrategyName TEXT, -- Denormalized for easy querying
                        Token TEXT DEFAULT ''
                    );

                    CREATE TABLE IF NOT EXISTS Positions (
                        Symbol TEXT PRIMARY KEY,
                        NetQty REAL DEFAULT 0,
                        AvgPrice REAL DEFAULT 0,
                        Ltp REAL DEFAULT 0, -- Snapshot
                        RealizedPnL REAL DEFAULT 0
                    );

                    CREATE TABLE IF NOT EXISTS Credentials (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        BrokerName TEXT NOT NULL,
                        ApiKey TEXT,
                        ClientCode TEXT,
                        Password TEXT,
                        TotpKey TEXT
                    );
                ";
                command.ExecuteNonQuery();
            }
        }
    }
}
