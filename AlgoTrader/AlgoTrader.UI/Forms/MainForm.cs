using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Windows.Forms;
using Krypton.Toolkit;
using Krypton.Navigator;
using AlgoTrader.Brokers;
using AlgoTrader.Brokers.AngelOne;
using AlgoTrader.Core.Enums;
using AlgoTrader.Core.Interfaces;
using AlgoTrader.Core.Models;
using AlgoTrader.Data;
using AlgoTrader.Data.Encryption;
using AlgoTrader.Data.Repositories;
using AlgoTrader.MarketData;
using AlgoTrader.Notify;
using AlgoTrader.OMS;
using AlgoTrader.Strategy;
using AlgoTrader.UI.Controls;
using AlgoTrader.UI.Theme;
using Serilog;
using ScottPlot;
using ScottPlot.WinForms;
using Color = System.Drawing.Color;
using FontStyle = System.Drawing.FontStyle;
using Label = System.Windows.Forms.Label;
using LabelStyle = Krypton.Toolkit.LabelStyle;

namespace AlgoTrader.UI.Forms;

public partial class MainForm : KryptonForm
{
    // ─── Services ───────────────────────────────────────────────────────────────
    private readonly IStrategyEngine _strategyEngine;
    private readonly IMarketDataService _marketData;
    private readonly IOrderManager _orderManager;
    private readonly IRiskManager _riskManager;
    private readonly IBrokerFactory _brokerFactory;
    private readonly DataBaseManager _dbManager;
    private readonly CredentialProtector _credentialProtector;
    private readonly AccountRepository _accountRepo;
    private readonly InAppAlertService _alertService;
    private readonly PositionTracker _positionTracker;
    private readonly StrategyEventBus _eventBus;
    private readonly SyncService _syncService;
    private readonly InstrumentMasterService _instrumentService;
    private readonly TickDispatcher _tickDispatcher;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;

    private readonly List<AccountCredential> _accounts = new();
    private System.Windows.Forms.Timer _clockTimer = null!;
    private ControlScaler _scaler;

    // ─── Layout Fields ──────────────────────────────────────────────────────────
    private static KryptonManager _globalManager;
    private KryptonManager    _kryptonManager;
    private KryptonNavigator  _navigator;
    private KryptonPanel      _sidebar;
    private KryptonPanel[]    _pages;

    // Arctic UI controls (replaces old KryptonPanel inline paints)
    private ArcticHeaderPanel _headerPanel   = null!;
    private Panel             _menuBar       = null!;
    private ArcticBottomBar   _bottomBarPanel = null!;
    private ArcticAlertsPanel _alertsPanel   = null!;

    // Live-data UI refs
    private List<Control>       _kpiCards      = new();
    private FormsPlot           _mtmPlot       = null!;
    private List<double>        _mtmHistoryX   = new();
    private List<double>        _mtmHistoryY   = new();
    private KryptonRichTextBox  _logBox        = null!;
    private KryptonDataGridView _positionGrid  = null!;
    private KryptonDataGridView _orderGrid     = null!;
    private KryptonDataGridView _strategyGrid  = null!;

    // Index data cached for header (updated by tick stream)
    private string _niftyLtp = "24,350.50", _niftyChg = "+0.19%"; private bool _niftyUp = true;
    private string _bnLtp    = "52,180.25", _bnChg    = "-0.31%"; private bool _bnUp    = false;
    private string _finLtp   = "23,890.00", _finChg   = "+0.07%"; private bool _finUp   = true;

    // ─── Constructor ────────────────────────────────────────────────────────────
    public MainForm(
        IStrategyEngine strategyEngine, IMarketDataService marketData,
        IOrderManager orderManager, IRiskManager riskManager,
        IBrokerFactory brokerFactory, DataBaseManager dbManager,
        CredentialProtector credentialProtector, AccountRepository accountRepo,
        InAppAlertService alertService, PositionTracker positionTracker,
        SyncService syncService, InstrumentMasterService instrumentService,
        TickDispatcher tickDispatcher, IServiceProvider serviceProvider, ILogger logger)
    {
        _strategyEngine      = strategyEngine;
        _marketData          = marketData;
        _orderManager        = orderManager;
        _riskManager         = riskManager;
        _brokerFactory       = brokerFactory;
        _dbManager           = dbManager;
        _credentialProtector = credentialProtector;
        _accountRepo         = accountRepo;
        _alertService        = alertService;
        _positionTracker     = positionTracker;
        _syncService         = syncService;
        _instrumentService   = instrumentService;
        _tickDispatcher      = tickDispatcher;
        _serviceProvider     = serviceProvider;
        _logger              = logger;
        _eventBus = _strategyEngine is StrategyEngine se ? se.EventBus : new StrategyEventBus();

        ApplyGlobalPalette();
        InitializeUI();
        LoadAccounts();
        SubscribeToEvents();
        RegisterKeyboardShortcuts();
        StartTimers();
        SetupUiLogging();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _scaler = new ControlScaler(this);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _scaler?.Scale();
    }

    private void SetupUiLogging()
    {
        UiLogSink.OnMessage += (msg) => {
            if (_logBox == null || _logBox.IsDisposed) return;
            if (_logBox.InvokeRequired) {
                try { _logBox.BeginInvoke(new Action(() => AppendLog(msg))); } catch { }
            } else {
                AppendLog(msg);
            }
        };
    }

    // ─── Global Palette ─────────────────────────────────────────────────────────
    private void ApplyGlobalPalette()
    {
        if (_globalManager != null) return;
        _globalManager = new KryptonManager();
        _globalManager.GlobalPaletteMode = PaletteMode.Microsoft365Blue;
    }

    // ─── Main Layout ────────────────────────────────────────────────────────────
    private void InitializeUI()
    {
        SuspendLayout();

        _kryptonManager = new KryptonManager();
        _kryptonManager.GlobalPaletteMode = PaletteMode.Microsoft365Blue;

        this.Text          = "Cognex Algo — Algorithmic Trading Terminal";
        try { this.Icon = new Icon(@"Assets\icon.ico"); } catch { }
        this.MinimumSize   = new Size(1280, 720);
        this.Size          = new Size(1440, 820);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor     = ArcticColors.WindowBg;

        // Header (DockStyle.Top, 48px)
        BuildHeaderPanel();

        // Bottom status bar (DockStyle.Bottom, 26px)
        BuildBottomBar();

        // Toolbar / menu (DockStyle.Top, 36px — inner)
        BuildMenuBar();

        // Main fill: sidebar + navigator
        var mainPanel = new KryptonPanel {
            Dock = DockStyle.Fill,
            PanelBackStyle = PaletteBackStyle.PanelClient
        };
        mainPanel.StateCommon.Color1 = ArcticColors.ContentBg;
        mainPanel.StateCommon.Color2 = ArcticColors.ContentBg;
        this.Controls.Add(mainPanel);

        // Sidebar (DockStyle.Left, 172px)
        _sidebar = new KryptonPanel {
            Dock = DockStyle.Left,
            Size = new Size(172, 0),
            PanelBackStyle = PaletteBackStyle.PanelAlternate
        };
        _sidebar.StateCommon.Color1 = ArcticColors.SidebarBg;
        _sidebar.StateCommon.Color2 = ArcticColors.SidebarBg;
        _sidebar.StateCommon.ColorStyle = PaletteColorStyle.Solid;
        _sidebar.Paint += (s, e) => {
            using var pen = new Pen(ArcticColors.BorderMedium);
            e.Graphics.DrawLine(pen, _sidebar.Width - 1, 0, _sidebar.Width - 1, _sidebar.Height);
        };
        BuildSidebarContent();
        mainPanel.Controls.Add(_sidebar);

        // Navigator (DockStyle.Fill — tabs + pages)
        _navigator = new KryptonNavigator {
            Dock = DockStyle.Fill,
            NavigatorMode = NavigatorMode.BarTabGroup,
            Bar = {
                TabBorderStyle  = TabBorderStyle.RoundedOutsizeMedium,
                TabStyle        = TabStyle.HighProfile,
                BarMapImage     = MapKryptonPageImage.None,
                ItemAlignment   = RelativePositionAlign.Near,
                BarOrientation  = VisualOrientation.Top,
                BarFirstItemInset = 8,
                BarLastItemInset  = 8
            }
        };
        StyleNavigator();

        // Build tab pages
        string[] tabNames = {
            "📊  Dashboard", "🎯  Strategies", "📋  Positions",
            "📑  Orders",    "📈  Charting",   "🔗  Option Chain", "🛠  Diagnostics"
        };
        _pages = new KryptonPanel[tabNames.Length];
        for (int i = 0; i < tabNames.Length; i++)
        {
            var page = new KryptonPage {
                Text = tabNames[i], TextTitle = tabNames[i],
                AutoHiddenSlideSize = new Size(200, 200)
            };
            page.ClearFlags(KryptonPageFlags.DockingAllowClose);
            _navigator.Pages.Add(page);

            var pagePanel = new KryptonPanel {
                Dock = DockStyle.Fill,
                PanelBackStyle = PaletteBackStyle.PanelClient
            };
            pagePanel.StateCommon.Color1 = ArcticColors.ContentBg;
            pagePanel.StateCommon.Color2 = ArcticColors.ContentBg;
            page.Controls.Add(pagePanel);
            _pages[i] = pagePanel;
        }
        _navigator.SelectedPageChanged += (s, e) => LoadTabContent(_navigator.SelectedIndex);
        mainPanel.Controls.Add(_navigator);
        _navigator.BringToFront();

        ResumeLayout(true);

        // Z-order: header → outermost top, menuBar → inner top,
        //          bottomBar → outermost bottom, mainPanel → fill
        _menuBar.SendToBack();
        _bottomBarPanel.SendToBack();
        _headerPanel.SendToBack();
        mainPanel.BringToFront();

        this.Shown += (s, e) => LoadTabContent(0);
    }

    private void StyleNavigator()
    {
        // Tab bar / header group background
        _navigator.StateNormal.HeaderGroup.Back.Color1 = ArcticColors.TabBarBg;
        _navigator.StateNormal.HeaderGroup.Back.Color2 = ArcticColors.TabBarBg;
        _navigator.StateNormal.HeaderGroup.Back.ColorStyle = PaletteColorStyle.Solid;
        _navigator.StateNormal.HeaderGroup.Border.Color1 = ArcticColors.BorderLight;

        // Normal tab
        _navigator.StateCommon.Tab.Content.ShortText.Font   = ArcticFonts.TabNormal;
        _navigator.StateCommon.Tab.Content.ShortText.Color1 = Color.FromArgb(100, 120, 160);
        _navigator.StateCommon.Tab.Back.Color1 = ArcticColors.TabBarBg;
        _navigator.StateCommon.Tab.Back.Color2 = ArcticColors.TabBarBg;
        _navigator.StateCommon.Tab.Back.ColorStyle = PaletteColorStyle.Solid;
        _navigator.StateCommon.Tab.Border.Color1 = ArcticColors.TabBorder;

        // Selected tab
        _navigator.StateSelected.Tab.Content.ShortText.Color1 = ArcticColors.AccentBlue;
        _navigator.StateSelected.Tab.Content.ShortText.Font   = ArcticFonts.TabActive;
        _navigator.StateSelected.Tab.Back.Color1 = ArcticColors.ActiveTabBg;
        _navigator.StateSelected.Tab.Back.Color2 = ArcticColors.ActiveTabBg;
        _navigator.StateSelected.Tab.Back.ColorStyle = PaletteColorStyle.Solid;
        _navigator.StateSelected.Tab.Border.Color1 = ArcticColors.TabBorder;

        // Hover tab
        _navigator.StateTracking.Tab.Content.ShortText.Color1 = ArcticColors.AccentBlue;
        _navigator.StateTracking.Tab.Back.Color1 = ArcticColors.HoverBg;
        _navigator.StateTracking.Tab.Back.Color2 = ArcticColors.HoverBg;
        _navigator.StateTracking.Tab.Back.ColorStyle = PaletteColorStyle.Solid;

        // Page content area background
        _navigator.StateCommon.HeaderGroup.Back.Color1 = ArcticColors.ContentBg;
        _navigator.StateCommon.HeaderGroup.Back.Color2 = ArcticColors.ContentBg;
        _navigator.StateCommon.HeaderGroup.Border.Color1 = ArcticColors.BorderLight;
    }

    // ─── Header ─────────────────────────────────────────────────────────────────
    private void BuildHeaderPanel()
    {
        // ArcticHeaderPanel owns its paint, pulse timer, and index pills
        _headerPanel = new ArcticHeaderPanel { Dock = DockStyle.Top };
        this.Controls.Add(_headerPanel);
    }

    // ─── Bottom Status Bar ──────────────────────────────────────────────────────
    private void BuildBottomBar()
    {
        _bottomBarPanel = new ArcticBottomBar { Dock = DockStyle.Bottom };
        this.Controls.Add(_bottomBarPanel);
    }

    // ─── Menu / Toolbar ─────────────────────────────────────────────────────────
    private void BuildMenuBar()
    {
        _menuBar = new Panel {
            Dock = DockStyle.Top,
            Height = 36,
            BackColor = ArcticColors.MenuBarBg
        };
        _menuBar.Paint += (s, e) => {
            using var pen = new Pen(ArcticColors.BorderLight);
            e.Graphics.DrawLine(pen, 0, 35, _menuBar.Width, 35);
        };

        int x = 10;

        ArcticMenuButton AddBtn(string label, Color dotColor, bool isActive = false)
        {
            var btn = new ArcticMenuButton {
                Icon     = "●",
                Text     = label,
                DotColor = dotColor,
                IsActive = isActive,
                Left     = x,
                Top      = 4,
                Height   = 26,
                Width    = TextRenderer.MeasureText(label, ArcticFonts.MenuBtn).Width + 42
            };
            _menuBar.Controls.Add(btn);
            x += btn.Width + 4;
            return btn;
        }
        void AddSep()
        {
            _menuBar.Controls.Add(new Panel {
                Left = x + 2, Top = 8, Width = 1, Height = 20,
                BackColor = ArcticColors.BorderLight
            });
            x += 12;
        }

        var btnLogin    = AddBtn("Login",        ArcticColors.AccentBlue);
        var btnLoginAll = AddBtn("Login All",     ArcticColors.AccentBlue);
        var btnStart    = AddBtn("▶ Start",       ArcticColors.ProfitGreenLt, isActive: true);
        var btnStop     = AddBtn("■ Stop",        ArcticColors.LossRed);
        var btnExit     = AddBtn("Exit All",      ArcticColors.WarningAmber);
        AddSep();
        var btnStrategy = AddBtn("+ Strategy",   Color.FromArgb(96, 64, 192));
        var btnChain    = AddBtn("Chain",         Color.FromArgb(0, 136, 170));
        var btnInstr    = AddBtn("Instruments",   Color.FromArgb(32, 136, 136));

        btnLogin.Click    += (s, e) => ShowLoginForm();
        btnLoginAll.Click += async (s, e) => await LoginAllAsync();
        btnStart.Click    += async (s, e) => await _strategyEngine.StartAsync();
        btnStop.Click     += async (s, e) => await _strategyEngine.StopAsync();
        btnExit.Click     += (s, e) => ExitAllPositions();
        btnStrategy.Click += (s, e) => new StrategyWizardForm().ShowDialog(this);
        btnChain.Click    += (s, e) => ShowOptionChain();
        btnInstr.Click    += async (s, e) => await LoadInstrumentMasterAsync();

        this.Controls.Add(_menuBar);
    }

    // ─── Sidebar ────────────────────────────────────────────────────────────────
    private void BuildSidebarContent()
    {
        _sidebar.Controls.Clear();

        var hdrLbl = new Label {
            Text = "ACCOUNTS", Left = 12, Top = 12, AutoSize = true,
            BackColor = Color.Transparent,
            Font = ArcticFonts.SectionLbl,
            ForeColor = ArcticColors.TextMuted
        };
        _sidebar.Controls.Add(hdrLbl);

        int y = 34;
        foreach (var acc in _accounts)
        {
            var accRef = acc;
            bool isFirst = (y == 34);
            var card = new ArcticAccountCard {
                AccountId   = acc.AccountName,
                BrokerName  = acc.BrokerType.ToString(),
                MTM         = 0m,
                IsConnected = false,
                Left        = 8,
                Top         = y,
                Width       = _sidebar.Width - 16,
                Selected    = isFirst
            };

            // Delete context menu
            var cms = new ContextMenuStrip();
            cms.Items.Add("Delete Account").Click += (s, e) => {
                if (MessageBox.Show($"Delete {accRef.AccountName}?", "Confirm",
                    MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    bool res = _accountRepo.Delete(accRef.ClientID);
                    if (!res && accRef.ClientID.Length == 24) {
                        try { _accountRepo.Delete(new LiteDB.ObjectId(accRef.ClientID)); } catch { }
                    }
                    LoadAccounts();
                }
            };
            card.ContextMenuStrip = cms;

            // Selection toggle
            card.Click += (s, e) => {
                foreach (Control c in _sidebar.Controls)
                    if (c is ArcticAccountCard ac) ac.Selected = false;
                ((ArcticAccountCard)s!).Selected = true;
            };

            _sidebar.Controls.Add(card);
            y += 76;
        }
    }

    // ─── Tab Content ────────────────────────────────────────────────────────────
    private void LoadTabContent(int idx)
    {
        if (_pages == null || idx < 0 || idx >= _pages.Length) return;
        var page = _pages[idx];
        page.SuspendLayout();
        foreach (Control c in page.Controls) c.Dispose();
        page.Controls.Clear();
        switch (idx)
        {
            case 0: BuildDashboard(page);   break;
            case 1: BuildStrategies(page);  break;
            case 2: BuildPositions(page);   break;
            case 3: BuildOrders(page);      break;
            case 4: BuildCharting(page);    break;
            case 5: BuildOptionChain(page); break;
            case 6: BuildDiagnostics(page); break;
        }
        page.ResumeLayout(true);
    }

    // ─── Dashboard ──────────────────────────────────────────────────────────────
    private void BuildDashboard(KryptonPanel parent)
    {
        parent.Padding = new Padding(0);
        _kpiCards.Clear();

        // KPI row (top, 106px)
        var kpiRow = new Panel {
            Dock = DockStyle.Top, Height = 106,
            BackColor = ArcticColors.ContentBg,
            Padding = new Padding(12, 8, 12, 8)
        };
        parent.Controls.Add(kpiRow);

        var kpiDefs = new[] {
            ("TOTAL MTM",       "₹0.00",  "Today's mark-to-market",  true,  false, ""),
            ("REALIZED P&L",    "₹0.00",  "Booked profit today",     true,  false, ""),
            ("OPEN POSITIONS",  "0",      "",                        false, true,  "2 Strategies Active"),
            ("TODAY TRADES",    "0",      "",                        false, true,  "8 Buy · 4 Sell"),
        };

        void LayoutKpiCards()
        {
            int n = _kpiCards.Count;
            if (n == 0 || kpiRow.ClientSize.Width == 0) return;
            int gap = 10, pad = 12;
            int w = (kpiRow.ClientSize.Width - pad * 2 - gap * (n - 1)) / n;
            for (int i = 0; i < n; i++) {
                _kpiCards[i].Left   = pad + i * (w + gap);
                _kpiCards[i].Top    = 4;
                _kpiCards[i].Width  = w;
                _kpiCards[i].Height = kpiRow.ClientSize.Height - 12;
            }
        }

        foreach (var (label, value, sub, isProfit, isBlue, tag) in kpiDefs)
        {
            var card = new ArcticKpiCard {
                Label    = label,
                Value    = value,
                SubText  = sub,
                IsProfit = isProfit,
                IsBlue   = isBlue,
                Tag      = tag
            };
            kpiRow.Controls.Add(card);
            _kpiCards.Add(card);
        }
        kpiRow.Resize += (s, e) => LayoutKpiCards();
        LayoutKpiCards();

        // Content below KPI row
        var contentBelow = new Panel {
            Dock = DockStyle.Fill,
            BackColor = ArcticColors.ContentBg,
            Padding = new Padding(12, 6, 12, 8)
        };
        parent.Controls.Add(contentBelow);
        contentBelow.BringToFront();

        // Alerts panel (bottom, 190px)
        _alertsPanel = new ArcticAlertsPanel {
            Dock   = DockStyle.Bottom,
            Height = 190,
            Title  = "Recent Signals & Alerts"
        };
        _alertsPanel.AddAlert("✅", "Iron Condor · NIFTY 24000CE Sell filled @ ₹148.50",
            "ENTRY", "15:22:08", AlertBadgeType.Entry);
        _alertsPanel.AddAlert("⚠️", "Strangle · MTM trailing SL moved to ₹18,500",
            "SL TRAIL", "14:55:33", AlertBadgeType.SL);
        _alertsPanel.AddAlert("🔴", "Bull Spread · Target ₹12,000 hit — Exiting all legs",
            "EXIT", "13:42:17", AlertBadgeType.Exit);
        contentBelow.Controls.Add(_alertsPanel);

        var spacer = new Panel {
            Dock = DockStyle.Bottom, Height = 8,
            BackColor = ArcticColors.ContentBg
        };
        contentBelow.Controls.Add(spacer);

        // Chart card (fills remaining space)
        var chartHdr = BuildSectionCard("Portfolio P&L Curve", "9:15 AM → 3:30 PM  ·  Today",
            DockStyle.Fill);
        contentBelow.Controls.Add(chartHdr);
        chartHdr.BringToFront();

        _mtmPlot = new FormsPlot {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(250, 252, 255)
        };
        StyleMtmPlot();
        chartHdr.Panel.Controls.Add(_mtmPlot);
    }

    // ─── Shared Card Builder ────────────────────────────────────────────────────
    private KryptonHeaderGroup BuildSectionCard(string heading, string description,
        DockStyle dock, int height = 0)
    {
        var hg = new KryptonHeaderGroup {
            Dock = dock,
            HeaderStylePrimary = HeaderStyle.Secondary,
            HeaderVisibleSecondary = false,
            GroupBackStyle = PaletteBackStyle.PanelClient
        };
        if (height > 0) hg.Height = height;

        hg.StateCommon.HeaderPrimary.Back.Color1 = ArcticColors.TabBarBg;
        hg.StateCommon.HeaderPrimary.Back.Color2 = ArcticColors.TabBarBg;
        hg.StateCommon.HeaderPrimary.Back.ColorStyle = PaletteColorStyle.Solid;
        hg.StateCommon.HeaderPrimary.Content.ShortText.Color1 = ArcticColors.TextHeader;
        hg.StateCommon.HeaderPrimary.Content.ShortText.Font   = ArcticFonts.ChartTitle;
        hg.StateCommon.HeaderPrimary.Content.LongText.Color1  = ArcticColors.TextMuted;
        hg.StateCommon.HeaderPrimary.Content.LongText.Font    = ArcticFonts.ChartSub;
        hg.StateCommon.HeaderPrimary.Border.Color1 = ArcticColors.BorderLight;
        hg.StateCommon.Back.Color1     = ArcticColors.ContentBg;
        hg.StateCommon.Back.Color2     = ArcticColors.ContentBg;
        hg.StateCommon.Border.Color1   = ArcticColors.BorderLight;
        hg.StateCommon.Border.DrawBorders = PaletteDrawBorders.All;
        hg.ValuesPrimary.Heading     = heading;
        hg.ValuesPrimary.Description = description;
        return hg;
    }

    private void StyleMtmPlot()
    {
        var bgColor = ScottPlot.Color.FromColor(Color.FromArgb(250, 252, 255));
        _mtmPlot.Plot.FigureBackground.Color = bgColor;
        _mtmPlot.Plot.DataBackground.Color   = bgColor;
        _mtmPlot.Plot.Axes.Left.Label.Text   = "MTM (₹)";
        _mtmPlot.Plot.Axes.Left.Label.ForeColor =
            ScottPlot.Color.FromColor(ArcticColors.TextLabel);
        _mtmPlot.Plot.Axes.Bottom.TickLabelStyle.ForeColor =
            ScottPlot.Color.FromColor(ArcticColors.TextMuted);
        _mtmPlot.Plot.Axes.Left.TickLabelStyle.ForeColor =
            ScottPlot.Color.FromColor(ArcticColors.TextMuted);
        _mtmPlot.Plot.Grid.MajorLineColor =
            ScottPlot.Color.FromColor(ArcticColors.ChartGrid);
        _mtmPlot.Plot.Title("");
    }

    // ─── Shared Helpers ─────────────────────────────────────────────────────────
    private KryptonLabel MakeSectionTitle(string text)
    {
        var lbl = new KryptonLabel {
            Dock = DockStyle.Top, Height = 40, Text = text
        };
        lbl.StateNormal.ShortText.Font   = new Font("Segoe UI", 11f, FontStyle.Bold);
        lbl.StateNormal.ShortText.Color1 = ArcticColors.TextPrimary;
        return lbl;
    }

    private KryptonDataGridView MakeStyledGrid()
    {
        var grid = new KryptonDataGridView {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AutoGenerateColumns = false,
            BackgroundColor = ArcticColors.ContentBg,
            GridColor = ArcticColors.BorderLight,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal
        };
        grid.StateCommon.Background.Color1 = ArcticColors.ContentBg;
        grid.StateCommon.Background.Color2 = ArcticColors.ContentBg;

        grid.StateCommon.HeaderColumn.Back.Color1    = ArcticColors.TabBarBg;
        grid.StateCommon.HeaderColumn.Back.Color2    = ArcticColors.TabBarBg;
        grid.StateCommon.HeaderColumn.Back.ColorStyle = PaletteColorStyle.Solid;
        grid.StateCommon.HeaderColumn.Content.Font   = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        grid.StateCommon.HeaderColumn.Content.Color1 = ArcticColors.TextNav;
        grid.StateCommon.HeaderColumn.Border.Color1  = ArcticColors.BorderLight;

        grid.StateCommon.DataCell.Back.Color1     = ArcticColors.CardBg;
        grid.StateCommon.DataCell.Back.Color2     = ArcticColors.CardBg;
        grid.StateCommon.DataCell.Back.ColorStyle = PaletteColorStyle.Solid;
        grid.StateCommon.DataCell.Content.Font    = new Font("Segoe UI", 9f);
        grid.StateCommon.DataCell.Content.Color1  = ArcticColors.TextPrimary;
        grid.StateCommon.DataCell.Content.Padding = new Padding(6, 0, 6, 0);
        grid.StateCommon.DataCell.Border.Color1   = ArcticColors.BorderLight;

        grid.StateSelected.DataCell.Back.Color1     = ArcticColors.ActiveBg;
        grid.StateSelected.DataCell.Back.Color2     = ArcticColors.ActiveBg;
        grid.StateSelected.DataCell.Back.ColorStyle = PaletteColorStyle.Solid;
        grid.StateSelected.DataCell.Content.Color1  = ArcticColors.AccentBlue;
        return grid;
    }

    // ─── Positions ──────────────────────────────────────────────────────────────
    private void BuildPositions(KryptonPanel parent)
    {
        parent.Padding = new Padding(12, 8, 12, 8);
        parent.StateCommon.Color1 = ArcticColors.ContentBg;

        var infoRow = new Panel {
            Dock = DockStyle.Top, Height = 42, BackColor = ArcticColors.ContentBg
        };
        var titleLbl = new KryptonLabel {
            Text = "Open Positions", Dock = DockStyle.Left,
            LabelStyle = LabelStyle.BoldControl
        };
        titleLbl.StateNormal.ShortText.Font   = new Font("Segoe UI", 11f, FontStyle.Bold);
        titleLbl.StateNormal.ShortText.Color1 = ArcticColors.TextPrimary;

        var sqBtn = new KryptonButton {
            Text = "■  Square Off All", Dock = DockStyle.Right, Width = 148
        };
        sqBtn.StateCommon.Back.Color1 = ArcticColors.LossRed;
        sqBtn.StateCommon.Back.Color2 = ArcticColors.LossRed;
        sqBtn.StateCommon.Content.ShortText.Color1 = Color.White;
        sqBtn.StateCommon.Content.ShortText.Font   = new Font("Segoe UI", 9f, FontStyle.Bold);
        sqBtn.Click += (s, e) => ExitAllPositions();
        infoRow.Controls.AddRange(new Control[] { titleLbl, sqBtn });
        parent.Controls.Add(infoRow);

        _positionGrid = MakeStyledGrid();
        string[] cols = { "Strategy", "Symbol", "Expiry", "Strike", "B/S", "Lots",
                          "Avg Price", "LTP", "P&L", "Status" };
        foreach (var c in cols)
            _positionGrid.Columns.Add(new DataGridViewTextBoxColumn {
                HeaderText = c,
                Name = c.Replace("/", "").Replace("&", "").Replace(" ", "")
            });
        _positionGrid.Columns["PL"].DefaultCellStyle.Font =
            new Font("Courier New", 9f, FontStyle.Bold);
        _positionGrid.Columns["BS"].DefaultCellStyle.Alignment =
            DataGridViewContentAlignment.MiddleCenter;

        _positionGrid.Rows.Add("Iron Condor", "NIFTY24000CE", "27-Feb-24", "24000",
            "SELL", "2", "₹148.50", "₹145.30", "₹640",  "Active");
        _positionGrid.Rows.Add("Iron Condor", "NIFTY24500PE", "27-Feb-24", "24500",
            "SELL", "2", "₹95.20",  "₹97.80",  "-₹520", "Active");
        foreach (DataGridViewRow r in _positionGrid.Rows)
        {
            string pl = r.Cells["PL"].Value?.ToString() ?? "";
            r.Cells["PL"].Style.ForeColor =
                pl.StartsWith("-") ? ArcticColors.LossRed : ArcticColors.ProfitGreen;
        }
        parent.Controls.Add(_positionGrid);
    }

    // ─── Orders ─────────────────────────────────────────────────────────────────
    private void BuildOrders(KryptonPanel parent)
    {
        parent.Padding = new Padding(12, 8, 12, 8);
        parent.StateCommon.Color1 = ArcticColors.ContentBg;
        parent.Controls.Add(MakeSectionTitle("Order Book — Today"));

        _orderGrid = MakeStyledGrid();
        string[] cols = { "Time", "Order ID", "Strategy", "Symbol",
                          "B/S", "Qty", "Price", "Status", "Fill Price" };
        foreach (var c in cols)
            _orderGrid.Columns.Add(new DataGridViewTextBoxColumn {
                HeaderText = c,
                Name = c.Replace(" ", "").Replace("/", "")
            });

        _orderGrid.Rows.Add("09:21:05", "ORD001", "Iron Condor",
            "NIFTY24000CE", "SELL", "100", "₹148.50", "COMPLETE", "₹148.50");
        _orderGrid.Rows.Add("09:21:07", "ORD002", "Iron Condor",
            "NIFTY24500PE", "SELL", "100", "₹95.20",  "COMPLETE", "₹95.20");
        foreach (DataGridViewRow r in _orderGrid.Rows)
        {
            string status = r.Cells["Status"].Value?.ToString() ?? "";
            r.Cells["Status"].Style.ForeColor =
                status == "COMPLETE"  ? ArcticColors.ProfitGreen :
                status == "REJECTED"  ? ArcticColors.LossRed     :
                                        ArcticColors.WarningAmber;
        }
        parent.Controls.Add(_orderGrid);
    }

    // ─── Strategies ─────────────────────────────────────────────────────────────
    private void BuildStrategies(KryptonPanel parent)
    {
        parent.Padding = new Padding(12, 8, 12, 8);
        parent.StateCommon.Color1 = ArcticColors.ContentBg;
        parent.Controls.Add(MakeSectionTitle("Active Strategies"));

        var emptyLbl = new KryptonLabel {
            Text = "No strategies configured yet.  Click '+ Strategy' in the toolbar to create one.",
            Dock = DockStyle.Fill, LabelStyle = LabelStyle.NormalControl
        };
        emptyLbl.StateNormal.ShortText.Color1 = ArcticColors.TextMuted;
        emptyLbl.StateNormal.ShortText.Font   = new Font("Segoe UI", 10f, FontStyle.Italic);
        emptyLbl.StateNormal.ShortText.TextH  = PaletteRelativeAlign.Center;
        emptyLbl.StateNormal.ShortText.TextV  = PaletteRelativeAlign.Center;
        parent.Controls.Add(emptyLbl);
    }

    // ─── Charting ───────────────────────────────────────────────────────────────
    private void BuildCharting(KryptonPanel parent)
    {
        parent.StateCommon.Color1 = ArcticColors.ContentBg;
        var lbl = new KryptonLabel {
            Text = "Charting — Connect to broker to view live charts",
            Dock = DockStyle.Fill, LabelStyle = LabelStyle.NormalControl
        };
        lbl.StateNormal.ShortText.Color1 = ArcticColors.TextMuted;
        lbl.StateNormal.ShortText.TextH  = PaletteRelativeAlign.Center;
        lbl.StateNormal.ShortText.TextV  = PaletteRelativeAlign.Center;
        parent.Controls.Add(lbl);
    }

    // ─── Option Chain ────────────────────────────────────────────────────────────
    private void BuildOptionChain(KryptonPanel parent)
    {
        parent.StateCommon.Color1 = ArcticColors.ContentBg;
        var lbl = new KryptonLabel {
            Text = "Option Chain — Login to broker to load live data",
            Dock = DockStyle.Fill, LabelStyle = LabelStyle.NormalControl
        };
        lbl.StateNormal.ShortText.Color1 = ArcticColors.TextMuted;
        lbl.StateNormal.ShortText.TextH  = PaletteRelativeAlign.Center;
        lbl.StateNormal.ShortText.TextV  = PaletteRelativeAlign.Center;
        parent.Controls.Add(lbl);
    }

    // ─── Diagnostics ────────────────────────────────────────────────────────────
    private void BuildDiagnostics(KryptonPanel parent)
    {
        parent.Padding = new Padding(12, 8, 12, 8);
        parent.StateCommon.Color1 = ArcticColors.ContentBg;
        parent.Controls.Add(MakeSectionTitle("Diagnostics — System Logs"));

        _logBox = new KryptonRichTextBox {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.FromArgb(250, 252, 255),
            Font = new Font("Consolas", 9f),
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both
        };
        _logBox.StateCommon.Content.Font   = new Font("Consolas", 9f);
        _logBox.StateCommon.Content.Color1 = ArcticColors.TextPrimary;
        _logBox.Text = string.Join("\r\n", UiLogSink.GetHistory());
        _logBox.SelectionStart = _logBox.Text.Length;
        _logBox.ScrollToCaret();
        parent.Controls.Add(_logBox);
    }

    private void AppendLog(string msg)
    {
        _logBox.AppendText(msg + "\r\n");
        _logBox.SelectionStart = _logBox.Text.Length;
        _logBox.ScrollToCaret();
        if (_logBox.Lines.Length > 1000)
            _logBox.Text = string.Join("\r\n", _logBox.Lines.Skip(100));
    }

    // ─── Account Loading ────────────────────────────────────────────────────────
    private void LoadAccounts()
    {
        if (_sidebar == null) return;
        _accounts.Clear();
        foreach (var acc in _accountRepo.FindAll())
        {
            var decrypted = _credentialProtector.UnprotectCredential(acc);
            _accounts.Add(decrypted);
        }
        BuildSidebarContent();
    }

    // ─── Event Subscriptions ────────────────────────────────────────────────────
    private void SubscribeToEvents()
    {
        // Index tick updates → push to ArcticHeaderPanel properties
        _tickDispatcher.GetBatchedStream()
            .ObserveOn(SynchronizationContext.Current!)
            .Subscribe(batch => {
                if (_headerPanel == null) return;
                bool changed = false;
                foreach (var tick in batch)
                {
                    if (tick.Symbol.Contains("NIFTY") &&
                        !tick.Symbol.Contains("BANKNIFTY") &&
                        !tick.Symbol.Contains("FINNIFTY"))
                    { _niftyLtp = tick.LTP.ToString("N2"); changed = true; }

                    if (tick.Symbol.Contains("BANKNIFTY"))
                    { _bnLtp = tick.LTP.ToString("N2"); changed = true; }

                    if (tick.Symbol.Contains("FINNIFTY"))
                    { _finLtp = tick.LTP.ToString("N2"); changed = true; }
                }
                if (changed)
                {
                    _headerPanel.NiftyLTP = _niftyLtp;
                    _headerPanel.BnLTP    = _bnLtp;
                    _headerPanel.FinLTP   = _finLtp;
                    // Pulse timer already redraws every 40ms
                }
            });

        // MTM history → ScottPlot update
        _positionTracker.MTMUpdates
            .Sample(TimeSpan.FromSeconds(1))
            .ObserveOn(SynchronizationContext.Current!)
            .Subscribe(summary => {
                _mtmHistoryX.Add(DateTime.Now.ToOADate());
                _mtmHistoryY.Add((double)_positionTracker.TotalMTM);
                if (_mtmHistoryX.Count > 500) {
                    _mtmHistoryX.RemoveAt(0);
                    _mtmHistoryY.RemoveAt(0);
                }
                if (_mtmPlot == null) return;
                _mtmPlot.Plot.Clear();
                var sig = _mtmPlot.Plot.Add.Scatter(_mtmHistoryX, _mtmHistoryY);
                sig.LineWidth = 2;
                sig.Color     = ScottPlot.Colors.RoyalBlue;
                sig.MarkerSize = 0;
                _mtmPlot.Plot.Axes.Bottom.TickGenerator =
                    new ScottPlot.TickGenerators.DateTimeAutomatic();
                _mtmPlot.Plot.Axes.AutoScale();
                _mtmPlot.Refresh();
            });

        // Live alerts
        _alertService.Alerts
            .ObserveOn(SynchronizationContext.Current!)
            .Subscribe(alert => {
                _alertsPanel?.AddAlert("🔔", alert.ToString(), "ALERT",
                    DateTime.Now.ToString("HH:mm:ss"), AlertBadgeType.Entry);
            });

        // Order updates → order grid
        _orderManager.OrderUpdates
            .ObserveOn(SynchronizationContext.Current!)
            .Subscribe(order => {
                if (_orderGrid == null) return;
                bool found = false;
                foreach (DataGridViewRow row in _orderGrid.Rows)
                {
                    if (row.Cells["OrderID"].Value?.ToString() == order.OrderID) {
                        row.Cells["Status"].Value = order.Status.ToString();
                        found = true; break;
                    }
                }
                if (!found && !string.IsNullOrEmpty(order.OrderID))
                    _orderGrid.Rows.Add(
                        order.OrderTime.ToString("HH:mm:ss"), order.OrderID,
                        "Unknown", order.Symbol, order.BuySell, order.OrderQty,
                        "0.00", order.Status, "0.00");
            });
    }

    // ─── Timers ─────────────────────────────────────────────────────────────────
    private void StartTimers()
    {
        var uiTimer = new System.Windows.Forms.Timer { Interval = 250 };
        uiTimer.Tick += (s, e) => RefreshLiveData();
        uiTimer.Start();
    }

    private void RefreshLiveData()
    {
        if (this.InvokeRequired) { this.Invoke(RefreshLiveData); return; }

        // Update KPI cards
        if (_kpiCards != null && _kpiCards.Count >= 3)
        {
            var totalMtm     = _positionTracker.TotalMTM;
            var realizedPnl  = _positionTracker.RealizedPnL;
            var openPosCount = _positionTracker.Positions.Sum(s => s.OpenLegs.Count);

            if (_kpiCards[0] is ArcticKpiCard c0)
                c0.UpdateValue((totalMtm >= 0 ? "+" : "") + $"₹{totalMtm:N2}",
                    totalMtm >= 0);
            if (_kpiCards[1] is ArcticKpiCard c1)
                c1.UpdateValue((realizedPnl >= 0 ? "+" : "") + $"₹{realizedPnl:N2}",
                    realizedPnl >= 0);
            if (_kpiCards[2] is ArcticKpiCard c2)
                c2.UpdateValue(openPosCount.ToString(), false);
        }

        // Update bottom bar
        if (_bottomBarPanel != null)
        {
            _bottomBarPanel.TicksPerSec = (int)_tickDispatcher.TicksPerSecond;
            _bottomBarPanel.LastTick    = _tickDispatcher.LastTickTime.ToString("HH:mm:ss");
            _bottomBarPanel.TotalMTM    = _positionTracker.TotalMTM;
            _bottomBarPanel.IsLive      = _marketData.IsConnected;
            _bottomBarPanel.Invalidate();
        }

        // Update header connection status
        if (_headerPanel != null)
            _headerPanel.IsLive = _marketData.IsConnected;

        _strategyGrid?.Invalidate();
        _positionGrid?.Invalidate();
        _orderGrid?.Invalidate();
    }

    // ─── Actions ────────────────────────────────────────────────────────────────
    private async void ShowLoginForm()
    {
        using var loginForm = new LoginForm(_brokerFactory, _accountRepo,
            _credentialProtector, _logger);
        if (loginForm.ShowDialog() == DialogResult.OK)
        {
            LoadAccounts();

            // CRITICAL: LoadAccounts() reloads from DB which strips JWTToken/FeedToken
            // ([JsonIgnore] fields are never persisted). Use AuthenticatedCredential
            // from the form — it still holds the live tokens from broker.LoginAsync().
            var cred = loginForm.AuthenticatedCredential;
            if (cred != null && !_marketData.IsConnected)
            {
                await _marketData.ConnectAsync(cred);
            }
        }
    }

    private async Task LoginAllAsync()
    {
        foreach (var acc in _accounts)
        {
            try {
                var broker  = _brokerFactory.Create(acc.BrokerType);
                bool success = await broker.LoginAsync(acc);
                if (success) {
                    var encrypted = _credentialProtector.ProtectCredential(acc);
                    _accountRepo.Upsert(encrypted);

                    if (!_marketData.IsConnected)
                    {
                        await _marketData.ConnectAsync(acc);
                    }
                }
            }
            catch (Exception ex) {
                _logger.Error(ex, "Login failed for {Account}", acc.AccountName);
            }
        }
        LoadAccounts();
    }

    private void ShowOptionChain()
    {
        if (!_instrumentService.IsLoaded) {
            MessageBox.Show("Load instruments first.", "Not Ready"); return;
        }
        try {
            var form = (OptionChainForm)_serviceProvider.GetService(typeof(OptionChainForm));
            form.Show();
        } catch { }
    }

    private async Task LoadInstrumentMasterAsync()
    {
        Cursor = Cursors.WaitCursor;
        try {
            await _instrumentService.LoadAsync();
            MessageBox.Show($"Loaded {_instrumentService.Count:N0} instruments.");
        }
        catch (Exception ex) { _logger.Error(ex, "Instrument load failed"); }
        finally { Cursor = Cursors.Default; }
    }

    private void ExitAllPositions()
    {
        if (MessageBox.Show("Confirm SQUARE OFF ALL strategies and positions?",
            "Confirm Exit All", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes)
        {
            _logger.Information("Square Off All requested from UI");
            Task.Run(async () => await _strategyEngine.SquareOffAllAsync());
        }
    }

    // ─── Keyboard Shortcuts ─────────────────────────────────────────────────────
    private void RegisterKeyboardShortcuts()
    {
        KeyPreview = true;
        KeyDown += (s, e) => {
            if (e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6) {
                int idx = (int)e.KeyCode - (int)Keys.D1;
                if (_navigator != null && idx < _navigator.Pages.Count)
                    _navigator.SelectedIndex = idx;
            }
        };
    }

    // ─── Cleanup ────────────────────────────────────────────────────────────────
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _clockTimer?.Stop();
        _syncService?.Stop();
        _tickDispatcher?.Dispose();
        base.OnFormClosing(e);
    }
}
