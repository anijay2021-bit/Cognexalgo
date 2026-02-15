import sqlite3
import json
import os

# Path to the specific build output DB
db_path = r'C:\Users\anijay\.gemini\antigravity\scratch\COGNEX\Cognexalgo.UI\bin\Debug\net8.0-windows\cognex.db'

print(f"Connecting to DB: {db_path}")

# Ensure dir exists
os.makedirs(os.path.dirname(db_path), exist_ok=True)

conn = sqlite3.connect(db_path)
cursor = conn.cursor()

# Ensure Table Exists (Schema from DatabaseService.cs)
cursor.execute('''
    CREATE TABLE IF NOT EXISTS Strategies (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        Name TEXT NOT NULL,
        Symbol TEXT NOT NULL,
        ProductType TEXT DEFAULT 'MIS',
        StrategyType TEXT NOT NULL, 
        Parameters JSON,
        IsActive INTEGER DEFAULT 0
    )
''')

# Clear existing test strategies
cursor.execute("DELETE FROM Strategies WHERE Name = 'TestStrat'")

# JSON Params matching DynamicStrategyConfig structure
rules_json = {
    "StrategyName": "TestStrat",
    "Symbol": "NIFTY",
    "Timeframe": "5min",
    "EntryRules": [
        {
            "Action": "BUY_CE",
            "Conditions": [
                {
                    "Indicator": "LTP",
                    "Operator": "GREATER_THAN",
                    "SourceType": "StaticValue",
                    "StaticValue": 0,
                    "Period": 14 
                }
            ]
        }
    ],
    "ExitRules": []
}

params_str = json.dumps(rules_json)

# Insert
cursor.execute('''
    INSERT INTO Strategies (Name, Symbol, ProductType, StrategyType, Parameters, IsActive)
    VALUES (?, ?, ?, ?, ?, ?)
''', ('TestStrat', 'NIFTY', 'MIS', 'CUSTOM', params_str, 1))

conn.commit()
print("Strategy Inserted: TestStrat (Active)")
conn.close()
