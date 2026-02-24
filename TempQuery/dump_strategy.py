import sqlite3
import sys

db_path = r"c:\Users\anijay\.gemini\antigravity\scratch\COGNEX\Cognexalgo.UI\bin\Debug\net8.0-windows\database\algo.db"
try:
    conn = sqlite3.connect(db_path)
    cur = conn.cursor()
    cur.execute("SELECT ConfigJson FROM HybridStrategies WHERE Name='21ema ce'")
    row = cur.fetchone()
    if row:
        print(row[0])
    else:
        print("Strategy not found in DB")
except Exception as e:
    print("Error:", e)
finally:
    if 'conn' in locals():
        conn.close()
