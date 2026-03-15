# COGNEX AlgoTrader

A professional algorithmic trading desktop application for Indian index options markets (Nifty, BankNifty, Finnifty). Built on C# .NET 8 WPF with Clean Architecture.

---

## Features

- Live trading via AngelOne SmartAPI (WebSocket + REST)
- Paper trading simulation with full order lifecycle
- Dynamic EMA Crossover Strategy — buys ATM CE/PE on 21 EMA crossover
- Calendar Spread Strategy with weekly rolls and hedge buying
- Visual Payoff Builder — construct multi-leg strategies with live payoff diagram
- Real-time option chain with Greeks (IV%, Delta, Theta, Vega, Gamma)
- Live P&L tracking and intraday sparkline chart
- Auto square-off and risk management (daily max loss / max profit)
- Telegram notifications on signals and RMS breaches

---

## Project Structure

```
Cognexalgo.sln
├── Cognexalgo.Core/        — Models, strategies, services, Greeks engine
├── Cognexalgo.UI/          — WPF dashboard (MVVM, CommunityToolkit)
├── AlgoTrader/
│   ├── AlgoTrader.OMS/         — Order management, paper broker
│   ├── AlgoTrader.Strategy/    — Strategy engine, entry/exit evaluators
│   ├── AlgoTrader.Brokers/     — AngelOne broker adapter
│   ├── AlgoTrader.MarketData/  — SmartStream WebSocket feed
│   └── AlgoTrader.RMS/         — Risk management system
└── Cognexalgo.Tests/       — Unit and integration tests
```

---

## Setup

1. **Clone the repository**
   ```
   git clone <repo-url>
   cd COGNEX
   ```

2. **Create your credentials file**
   ```
   cp Cognexalgo.UI/appsettings.template.json Cognexalgo.UI/appsettings.json
   ```

3. **Fill in your credentials** — edit `Cognexalgo.UI/appsettings.json` with your AngelOne API key, client code, password, TOTP secret, and (optionally) Supabase connection string and Telegram bot token.

4. **Build the solution**
   ```
   dotnet build Cognexalgo.sln
   ```
   Or open `Cognexalgo.sln` in Visual Studio 2022 and build.

5. **Run**
   ```
   dotnet run --project Cognexalgo.UI
   ```
   Or press F5 in Visual Studio.

---

## Requirements

- Windows 10 / 11
- .NET 8 SDK
- AngelOne trading account with SmartAPI access enabled
- Visual Studio 2022 (or any IDE with .NET 8 support)

---

## Supported Strategies

| Strategy | Type | Description |
|---|---|---|
| Dynamic EMA Crossover | Intraday | Buys ATM CE or PE on 21 EMA crossover signal |
| Calendar Spread | Positional | Monthly long straddle + weekly short straddle with auto-rolls |
| Custom (Payoff Builder) | Any | Build and deploy arbitrary multi-leg strategies visually |

---

## Configuration Reference

All configuration lives in `Cognexalgo.UI/appsettings.json` (not committed — see `appsettings.template.json`):

| Section | Key | Description |
|---|---|---|
| `AngelOne` | `ApiKey` | SmartAPI app key |
| `AngelOne` | `ClientCode` | Your login ID |
| `AngelOne` | `Password` | Trading password |
| `AngelOne` | `TotpSecret` | TOTP authenticator secret |
| `ConnectionStrings` | `AlgoDatabase` | Postgres / Supabase connection string |
| `Telegram` | `BotToken` | Telegram bot token (optional) |
| `Telegram` | `ChatId` | Telegram chat ID (optional) |
| `V2.TradingConfig` | `DefaultMode` | `PaperTrade` or `LiveTrade` |
| `V2.AccountRms` | `DailyMaxLoss` | Daily max loss in INR before auto-exit |

---

## Important — Credentials Security

> **Never commit `appsettings.json` with real credentials.**
> The file is listed in `.gitignore` and will not be tracked by git.
> Use `appsettings.template.json` as the shareable configuration reference.

---

## License

Private — All rights reserved.
