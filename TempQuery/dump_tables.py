import sqlite3

db_path = r"c:\Users\anijay\.gemini\antigravity\scratch\COGNEX\Cognexalgo.UI\bin\Debug\net8.0-windows\database\algo.db"
try:
    conn = sqlite3.connect(db_path)
    cur = conn.cursor()
    cur.execute("SELECT name FROM sqlite_master WHERE type='table'")
    tables = cur.fetchall()
    print("Tables:", [t[0] for t in tables])
    
    # Try fetching from a likely table if it exists
    for table in ["HybridStrategies", "HybridStrategyEntities", "Strategies"]:
        if any(table == t[0] for t in tables):
            cur.execute(f"SELECT ConfigJson FROM {table} WHERE Name='21ema ce'")
            row = cur.fetchone()
            if row:
                print(f"\nFound in {table}:")
                print(row[0])
                break
    else:
        print("\nConfig not found in expected tables.")
except Exception as e:
    print("Error:", e)
finally:
    if 'conn' in locals():
        conn.close()
