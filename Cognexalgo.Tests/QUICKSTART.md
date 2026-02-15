# Quick Start Guide - Running the Closest Premium Test

## ✅ Build Status: READY

The test project is now properly configured and built successfully.

---

## Step 1: Add Your Angel One Credentials

Edit the file: `Cognexalgo.Tests/appsettings.json`

Replace the placeholder values:

```json
{
  "AngelOne": {
    "ApiKey": "your_actual_api_key_here",
    "ClientCode": "your_client_code_here",
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

### Where to Get Your Credentials:

1. **API Key & Client Code:**
   - Login to [Angel One SmartAPI Portal](https://smartapi.angelbroking.com/)
   - Navigate to "My Profile" → "API Keys"
   - Copy your API Key and Client Code

2. **TOTP Secret:**
   - In SmartAPI portal, enable TOTP
   - Save the base32-encoded TOTP secret (NOT the 6-digit code)

---

## Step 2: Run the Test

Open terminal in the `COGNEX` directory and run:

```bash
cd Cognexalgo.Tests
dotnet run
```

**OR** run directly from the COGNEX root:

```bash
dotnet run --project Cognexalgo.Tests
```

---

## Step 3: Review the Output

The test will display:

### ✅ Connection Status
```
STEP 1: ANGEL ONE CONNECTION
✅ Connected to Angel One API
⏱️  Latency: 1234ms
```

### ✅ Spot Price
```
STEP 3: SPOT PRICE FETCHING
✅ NIFTY Spot Price: ₹23,456.78
⏱️  Latency: 123ms
```

### ✅ Option Chain Build
```
STEP 4: OPTION CHAIN BUILDING
✅ Option Chain Built: 150 options available
⏱️  Latency: 3456ms (3.46s)
📊 Average per option: 23.04ms
```

### ✅ Closest Premium Match
```
STEP 5: CLOSEST PREMIUM SCAN (Target: ₹50.00)
✅ MATCH FOUND!
Selected Strike: 23600
Selected Premium: ₹49.75
Symbol: NIFTY13FEB2623600CE
Token: 12345
```

### ✅ Performance Summary
```
PERFORMANCE SUMMARY
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

---

## What to Validate:

1. **Latency:** Check if "Option chain build time" is < 2000ms (Excellent) or 2000-5000ms (Moderate)
2. **Strike Accuracy:** Cross-verify the selected strike against your Angel One terminal
3. **Symbol Token:** Ensure the token matches the actual instrument

---

## Troubleshooting:

### "Configuration file not found"
- ✅ **FIXED:** The .csproj now copies appsettings.json to output directory
- Run `dotnet build Cognexalgo.Tests` to rebuild

### "Please update appsettings.json with your actual credentials"
- Replace all `YOUR_*` placeholders with real values

### "Login Error: Invalid credentials"
- Verify API Key, Client Code, and Password
- Check TOTP Secret is base32 encoded (not the 6-digit code)

### "No options found"
- Check if market is open (9:15 AM - 3:30 PM IST)
- Try different expiry type (Weekly/Monthly)

---

## After Testing:

Once you confirm:
- ✅ Latency is acceptable
- ✅ Strike selection is accurate
- ✅ Symbol token mapping is correct

We'll proceed immediately to **Task 2: Database & Serialization** to make these strategies permanent!
