# SYNCUI Design Comparison

## How to choose

Open both XAML files in VS Code or Visual Studio to review side-by-side.
Tell me which one you want and I will wire it into the live project.

---

## Design 1 — Fluent Dark Terminal

**File:** `Design1/MainWindow.xaml`
**Theme:** `FluentDark` (Syncfusion)
**Window:** `SfChromelessWindow` — removes Windows titlebar, adds custom title chrome

### Visual concept
```
╔════════════════════════════════════════════════════╗
║  [logo]                    MTM ₹+12,450  09:32:14 ║  ← Custom chrome
╠════╦═══════════════════════════════════════════════╣
║  ⊞ ║  DASHBOARD                     ● 3 LIVE     ║  ← Slim top status bar
║ Dash║  ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐ ┌──────┐ ║
║  ◈ ║  │MTM  │ │STRAT│ │POS  │ │PEND │ │MAXDD│  ║  ← 5 KPI cards
║Strat║  │₹12k │ │  3  │ │  6  │ │  1  │ │₹2k  │  ║
║  ▤ ║  └─────┘ └─────┘ └─────┘ └─────┘ └──────┘  ║
║ Pos ║  ┌──────────────────────┐ ┌──────────────┐  ║
║  ⇄ ║  │  P&L Chart (area)    │ │ Live Strats  │  ║  ← Chart + live list
║Orders║  │  (SfChart)           │ │ (SfDataGrid) │  ║
║  Ω ║  └──────────────────────┘ └──────────────┘  ║
║Chain║                                              ║
║  ↗ ╠═══════════════════════════════════════════════╣
║ P&L ║  SYSTEM LOG  [CLEAR]                         ║  ← Log panel
║  ⊙ ║  > 09:32:10 INFO  Strategy NIFTY PE started  ║
║ A/c ║  > 09:32:14 INFO  Signal consumed: Entry     ║
╚════╩═══════════════════════════════════════════════╝
```

### Characteristics
- **Left nav rail** (68px) with icon + mini-label buttons — navigation replaces tab strip
- **No visible tab headers** — nav rail drives page switching
- **GitHub dark** colour palette (#0D1117 base, #58A6FF accent)
- **Pulsing green dot** in top bar for active strategies
- **Consolas log panel** — monospaced terminal feel
- **SfChromelessWindow** — the whole title bar is replaced with a custom branded chrome
- **GridLinesVisibility = Horizontal** — rows only, clean grid feel

### Best for
Traders who want maximum data density, minimal chrome, Bloomberg/Kite terminal aesthetic

---

## Design 2 — Material 3 Dark Dashboard

**File:** `Design2/MainWindow.xaml`
**Theme:** `Material3Dark` (Syncfusion)
**Window:** Standard Window with full Windows chrome

### Visual concept
```
╔══════════════════════════════════════════════════════════╗
║ File  View  Settings  Help                               ║  ← Windows titlebar
╠══════════════════════════════════════════════════════════╣
║  [logo]  │  ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐ ┌──┐  ║  ← Hero header
║COGNEXALGO│  │ MTM  │ │ACTIVE│ │POSIT.│ │PEND. │ │EX│  ║
║LIVE OPS  │  │₹+12k │ │● 3   │ │  6   │ │  1   │ │2 │  ║  ← 5 KPI tiles (rounded)
║          │  └──────┘ └──────┘ └──────┘ └──────┘ └──┘  ║
╠══════════════════════════════════════════════════════════╣
║ ⊞ Dashboard │ ◈ Strategies │ ▤ Positions │ ⇄ Orders ... ║  ← Tab nav bar (M3 style)
╠══════════════════════════════════════════════════════════╣
║                                                          ║
║   ┌────────────────────────────┐  ┌──────────────────┐  ║
║   │  Intraday P&L  ₹+12,450   │  │ Live Strategies  │  ║  ← Card-based layout
║   │  ┌──────────────────────┐  │  │ ┌──────────────┐ │  ║
║   │  │ SfChart area series  │  │  │ │ SfDataGrid   │ │  ║
║   │  │ (teal gradient fill) │  │  │ └──────────────┘ │  ║
║   │  └──────────────────────┘  │  │ [+ New] [Reports]│  ║
║   └────────────────────────────┘  └──────────────────┘  ║
╠══════════════════════════════════════════════════════════╣
║  System Log  [Clear]                                     ║  ← Log panel
║  > 09:32 INFO  Signal consumed: Entry                    ║
╚══════════════════════════════════════════════════════════╝
```

### Characteristics
- **Top horizontal tab bar** with icon + text labels (M3 style underline indicator)
- **Hero header** — large KPI tiles with drop shadows and rounded corners
- **Material 3** colour system: surface hierarchy (#1A1A2E → #16213E → #0F3460)
- **Primary accent**: Cyan/teal `#4FC3F7`
- **Pill buttons** (CornerRadius=20) — Material 3 filled + tonal button styles
- **Card-based layout** — all content in rounded elevated cards (CornerRadius=12, drop shadow)
- **Cascadia Code** in log panel — modern monospace font
- **Search box** in Strategies toolbar (pill shape, CornerRadius=24)
- **GridLinesVisibility = Horizontal** on all grids

### Best for
Traders who want a modern, spacious, fintech-app look — more visual breathing room

---

## Both designs share
- `SfDataGrid` replacing all `DataGrid` — better performance, built-in filtering/sorting UI
- All existing bindings preserved (same ViewModel, same properties)
- Same log panel at bottom with GridSplitter
- Same Kill Switch button
- Same tab content (Dashboard / Strategies / Positions / Orders / Option Chain / Analytics / Accounts)

## To implement your chosen design
Tell me "go with Design 1" or "go with Design 2" and I will:
1. Add Syncfusion DLL references to `Cognexalgo.UI.csproj`
2. Replace `MainWindow.xaml` with the chosen design
3. Update `MainWindow.xaml.cs` for `SfChromelessWindow` (Design 1 only)
4. Add `SfSkinManager` initialization in `App.xaml.cs`
5. Verify build compiles clean
