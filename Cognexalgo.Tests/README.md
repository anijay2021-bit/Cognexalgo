# Closest Premium Test - Setup Guide

## Prerequisites
- Angel One trading account
- API credentials from Angel One SmartAPI

## Setup Instructions

### 1. Get Your Angel One Credentials

1. **API Key & Client Code:**
   - Login to [Angel One SmartAPI Portal](https://smartapi.angelbroking.com/)
   - Navigate to "My Profile" → "API Keys"
   - Generate a new API key if you don't have one
   - Note down your **API Key** and **Client Code**

2. **TOTP Secret:**
   - In the SmartAPI portal, enable TOTP (Time-based One-Time Password)
   - Save the **TOTP Secret** (base32 encoded string)
   - This is used to generate 2FA codes programmatically

### 2. Configure the Test

1. Copy `appsettings.template.json` to `appsettings.json`:
   ```bash
   copy appsettings.template.json appsettings.json
   ```

2. Edit `appsettings.json` and replace the placeholder values:
   ```json
   {
     "AngelOne": {
       "ApiKey": "your_actual_api_key",
       "ClientCode": "your_client_code",
       "Password": "your_angel_one_password",
       "TotpSecret": "your_totp_secret_base32"
     },
     "Testing": {
       "TargetPremium": 50.0,
       "Index": "NIFTY",
       "ExpiryType": "Weekly"
     }
   }
   ```

3. **IMPORTANT:** `appsettings.json` is gitignored to prevent credential leakage

### 3. Run the Test

```bash
cd Cognexalgo.Tests
dotnet run
```

## What the Test Does

1. **Connects to Angel One API** using your credentials
2. **Loads Scrip Master** (instrument database)
3. **Fetches Spot Price** for the configured index (NIFTY/BANKNIFTY)
4. **Builds Option Chain** with real-time LTP data
5. **Performs Closest Premium Scan** to find strikes matching target premium
6. **Tracks Latency** for each operation to measure API performance

## Expected Output

```
================================================================================
CLOSEST PREMIUM SCAN - REAL API DEMONSTRATION
Angel One API Integration Test with Latency Tracking
================================================================================

Test Parameters:
  - Index: NIFTY
  - Expiry Type: Weekly
  - Target Premium: ₹50.00

================================================================================
STEP 1: ANGEL ONE CONNECTION
================================================================================
✅ Connected to Angel One API
⏱️  Latency: 1234ms

================================================================================
STEP 4: OPTION CHAIN BUILDING
================================================================================
✅ Option Chain Built: 150 options available
⏱️  Latency: 3456ms (3.46s)
📊 Average per option: 23.04ms

================================================================================
STEP 5: CLOSEST PREMIUM SCAN (Target: ₹50.00)
================================================================================
✅ MATCH FOUND!
--------------------------------------------------------------------------------
Selected Strike: 23600
Selected Premium: ₹49.75
Difference from Target: ₹0.25
Symbol: NIFTY13FEB2623600CE
Token: 12345
Lot Size: 50
--------------------------------------------------------------------------------

================================================================================
PERFORMANCE SUMMARY
================================================================================
Total Execution Time: 5678ms (5.68s)

Breakdown:
  - API Connection:       1234ms
  - Scrip Master Load:    3000ms
  - Spot Price Fetch:      123ms
  - Option Chain Build:   3456ms (3.46s)
  - Strike Calculation:      2ms

💡 SLIPPAGE ANALYSIS:
  - Option chain latency: 3456ms
  ⚠️  MODERATE: Acceptable for paper trading, optimize for live
```

## Latency Benchmarks

- **Excellent:** < 2000ms (suitable for live trading)
- **Moderate:** 2000-5000ms (acceptable for paper trading)
- **High:** > 5000ms (needs optimization)

## Troubleshooting

### "Failed to load configuration"
- Ensure `appsettings.json` exists in the output directory
- Check JSON syntax is valid

### "Please update appsettings.json with your actual credentials"
- Replace all `YOUR_*` placeholders with real values

### "Login Error: Invalid credentials"
- Verify API Key, Client Code, and Password are correct
- Check TOTP Secret is the base32 encoded string (not the 6-digit code)

### "No options found"
- Check if market is open (9:15 AM - 3:30 PM IST)
- Verify Scrip Master loaded successfully
- Try different expiry type (Weekly/Monthly)

## Security Notes

- ✅ `appsettings.json` is gitignored
- ✅ Never commit credentials to version control
- ✅ Use `appsettings.template.json` for sharing configuration structure
- ⚠️ Keep your TOTP Secret secure - it bypasses 2FA

## Next Steps

Once you confirm the test shows correct strikes:
1. Review latency metrics for optimization opportunities
2. Proceed to **Task 2: Database & Serialization**
3. Make strategies permanent in PostgreSQL
