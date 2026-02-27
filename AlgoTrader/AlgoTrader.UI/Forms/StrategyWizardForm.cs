using System;
using System.Drawing;
using System.Windows.Forms;
using Krypton.Toolkit;
using Krypton.Navigator;

namespace AlgoTrader.UI.Forms
{
public partial class StrategyWizardForm : KryptonForm
{
    // ── Theme colors ──────────────────────────────────────────────────────────
    static readonly Color ACCENT  = Color.FromArgb(21, 101, 192);
    static readonly Color NAVTEXT = Color.FromArgb(26, 58, 96);
    static readonly Color LABEL   = Color.FromArgb(96, 144, 184);
    static readonly Color PROFIT  = Color.FromArgb(27, 110, 56);
    static readonly Color LOSS    = Color.FromArgb(198, 40, 40);
    static readonly Color PAGEBG  = Color.FromArgb(247, 251, 255);
    static readonly Color INDBAR  = Color.FromArgb(232, 240, 253);

    // ── Indicator metadata ────────────────────────────────────────────────────
    static readonly string[] INDICATORS = {
        "RSI", "EMA", "SMA", "MACD", "VWAP",
        "ATR", "Supertrend", "Bollinger %B",
        "Stochastic %K", "ADX", "CCI", "OBV"
    };

    static readonly string[] IND_CONDITIONS = {
        "Crosses Above ↑", "Crosses Below ↓",
        "Is Above  >",     "Is Below  <",
        "Is Between",      "Equals"
    };

    static readonly string[] IND_SOURCES = {
        "Close", "Open", "High", "Low", "Volume", "HL/2"
    };

    private KryptonNavigator _wizard;
    private KryptonPanel[]   _wizardPages;
    private int              _legCount;
    private FlowLayoutPanel? _legContainer;
    private ControlScaler?   _scaler;

    private readonly string[] _pageNames = {
        "⚙  General Setup",    "🦵  Multi-Leg Builder",
        "🟢  Entry Conditions", "🔴  Exit Conditions",
        "🛡  Risk Management",  "🔁  Advanced Re-entry"
    };

    public StrategyWizardForm()
    {
        Text           = "Cognex Algo — Strategy Wizard";
        try { Icon = new Icon(@"Assets\icon.ico"); } catch { }
        Size           = new Size(1100, 820);
        MinimumSize    = new Size(1000, 700);
        StartPosition  = FormStartPosition.CenterParent;
        DoubleBuffered = true;
        new KryptonManager { GlobalPaletteMode = PaletteMode.Microsoft365Blue };
        BuildWizard();
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

    // ─────────────────────────────────────────────────────────────────────────
    private void BuildWizard()
    {
        SuspendLayout();

        // ── Bottom button bar ─────────────────────────────────────────────
        var btnBar = new KryptonPanel {
            Dock = DockStyle.Bottom, Height = 60,
            PanelBackStyle = PaletteBackStyle.PanelAlternate,
            Padding = new Padding(20, 12, 20, 12)
        };

        var saveBtn = new KryptonButton {
            Text = "💾  Save Strategy", Dock = DockStyle.Right,
            Width = 170, ButtonStyle = ButtonStyle.Standalone
        };
        saveBtn.StateCommon.Content.ShortText.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        saveBtn.Click += (s, e) => {
            KryptonMessageBox.Show(this, "Strategy saved!", "Saved",
                KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);
            Close();
        };

        var cancelBtn = new KryptonButton {
            Text = "Cancel", Dock = DockStyle.Right,
            Width = 96, ButtonStyle = ButtonStyle.LowProfile,
            Margin = new Padding(0, 0, 8, 0)
        };
        cancelBtn.Click += (s, e) => Close();

        var prevBtn = new KryptonButton {
            Text = "◀  Back", Dock = DockStyle.Left,
            Width = 96, ButtonStyle = ButtonStyle.LowProfile
        };
        prevBtn.Click += (s, e) => {
            if (_wizard.SelectedIndex > 0) _wizard.SelectedIndex--;
        };

        var nextBtn = new KryptonButton {
            Text = "Next  ▶", Dock = DockStyle.Left,
            Width = 96, ButtonStyle = ButtonStyle.Standalone,
            Margin = new Padding(8, 0, 0, 0)
        };
        nextBtn.Click += (s, e) => {
            if (_wizard.SelectedIndex < _wizardPages.Length - 1)
                _wizard.SelectedIndex++;
        };

        btnBar.Controls.AddRange(new Control[] { saveBtn, cancelBtn, nextBtn, prevBtn });
        Controls.Add(btnBar);

        // ── Wizard navigator (left tab strip) ────────────────────────────
        _wizard = new KryptonNavigator {
            Dock = DockStyle.Fill,
            NavigatorMode = NavigatorMode.BarTabGroup,
            Bar = {
                TabBorderStyle   = TabBorderStyle.SquareEqualMedium,
                TabStyle         = TabStyle.HighProfile,
                BarOrientation   = VisualOrientation.Left,
                ItemAlignment    = RelativePositionAlign.Near,
                BarMinimumHeight = 34
            }
        };

        _wizardPages = new KryptonPanel[_pageNames.Length];
        for (int i = 0; i < _pageNames.Length; i++)
        {
            var page = new KryptonPage {
                Text = _pageNames[i], TextTitle = _pageNames[i]
            };
            page.ClearFlags(KryptonPageFlags.DockingAllowClose);
            _wizard.Pages.Add(page);

            var panel = new KryptonPanel {
                Dock = DockStyle.Fill,
                PanelBackStyle = PaletteBackStyle.PanelClient
            };
            panel.StateCommon.Color1 = PAGEBG;
            panel.StateCommon.Color2 = PAGEBG;
            page.Controls.Add(panel);
            _wizardPages[i] = panel;
        }

        Controls.Add(_wizard);
        ResumeLayout(true);

        for (int i = 0; i < _wizardPages.Length; i++)
            BuildPage(i);

        _wizard.SelectedIndex = 0;
    }

    // ── Page shell ───────────────────────────────────────────────────────────
    private void BuildPage(int idx)
    {
        var panel = _wizardPages[idx];

        var hdr = new KryptonHeaderGroup {
            Dock = DockStyle.Top, Height = 68,
            HeaderStylePrimary = HeaderStyle.DockActive,
            HeaderVisibleSecondary = false
        };
        hdr.ValuesPrimary.Heading     = _pageNames[idx];
        hdr.ValuesPrimary.Description = GetPageSubtitle(idx);
        panel.Controls.Add(hdr);

        var scroll = new System.Windows.Forms.Panel {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = PAGEBG,
            Padding = new Padding(20, 16, 20, 16)
        };
        panel.Controls.Add(scroll);
        scroll.BringToFront();

        switch (idx) {
            case 0: FillGeneralSetup(scroll);  break;
            case 1: FillMultiLegPage(scroll);  break;
            case 2: FillEntry(scroll);         break;
            case 3: FillExit(scroll);          break;
            case 4: FillRisk(scroll);          break;
            case 5: FillReEntry(scroll);       break;
        }
    }

    private string GetPageSubtitle(int idx) => idx switch {
        0 => "Configure strategy name, symbol and trading hours",
        1 => "Define option legs — CE, PE or Futures",
        2 => "Set order mechanics and indicator trigger conditions for entry",
        3 => "Set order mechanics and indicator trigger conditions for exit",
        4 => "MTM target, stop-loss, trailing and lock profit rules",
        5 => "Re-entry rules, time windows and premium matching",
        _ => ""
    };

    // ── LAYOUT HELPERS ───────────────────────────────────────────────────────

    private const int ROW_H = 86;

    private TableLayoutPanel MakeGrid(int rows)
    {
        var g = new TableLayoutPanel {
            ColumnCount = 2,
            RowCount    = rows,
            Height      = rows * ROW_H,
            Anchor      = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Location    = new Point(0, 0),
            BackColor   = Color.Transparent,
            Padding     = new Padding(0, 0, 0, 4)
        };
        g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        for (int i = 0; i < rows; i++)
            g.RowStyles.Add(new RowStyle(SizeType.Absolute, ROW_H));
        return g;
    }

    private Panel Field(string label, Control ctrl, int rightPad = 12)
    {
        var p = new Panel {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };

        var lbl = new KryptonLabel {
            Text      = label,
            AutoSize  = false,
            Location  = new Point(0, 6),
            Height    = 22
        };
        lbl.StateNormal.ShortText.Font   = new Font("Segoe UI", 8f, FontStyle.Bold);
        lbl.StateNormal.ShortText.Color1 = LABEL;

        ctrl.Location = new Point(0, 30);
        ctrl.Height   = 32;

        p.Controls.Add(lbl);
        p.Controls.Add(ctrl);

        void UpdateWidths()
        {
            int w = Math.Max(p.ClientSize.Width - rightPad, 20);
            lbl.Width  = w;
            ctrl.Width = w;
        }
        p.Resize            += (s, e) => UpdateWidths();
        p.ClientSizeChanged += (s, e) => UpdateWidths();
        p.Paint             += (s, e) => { if (lbl.Width < 20) UpdateWidths(); };
        return p;
    }

    private Panel CheckCell(string text)
    {
        var p = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        var chk = new KryptonCheckBox {
            Text     = text,
            AutoSize = true,
            Location = new Point(6, 28)
        };
        chk.StateCommon.ShortText.Font = new Font("Segoe UI", 9f);
        p.Controls.Add(chk);
        return p;
    }

    // Section separator: bold label + 2px accent underline
    private Panel SectionSep(string text, int yOffset)
    {
        var p = new Panel {
            Height    = 36,
            Location  = new Point(0, yOffset),
            Anchor    = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = PAGEBG
        };

        var lbl = new KryptonLabel {
            Text = text, AutoSize = false,
            Location = new Point(0, 4), Height = 22
        };
        lbl.StateNormal.ShortText.Font   = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        lbl.StateNormal.ShortText.Color1 = ACCENT;

        var line = new Panel {
            Height = 2, BackColor = ACCENT,
            Location = new Point(0, 30)
        };

        void UpdateWidths()
        {
            lbl.Width  = Math.Max(p.ClientSize.Width, 100);
            line.Width = Math.Max(p.ClientSize.Width, 100);
        }
        p.Resize += (s, e) => UpdateWidths();
        p.Paint  += (s, e) => { if (lbl.Width < 100) UpdateWidths(); };
        p.Controls.Add(lbl);
        p.Controls.Add(line);
        return p;
    }

    // ── CONTROL FACTORIES ────────────────────────────────────────────────────

    private KryptonComboBox Combo(params string[] items)
    {
        var c = new KryptonComboBox {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Height = 32
        };
        c.Items.AddRange(items);
        if (c.Items.Count > 0) c.SelectedIndex = 0;
        return c;
    }

    private KryptonNumericUpDown Num(decimal min = 0, decimal max = 9_999_999,
        decimal val = 0, int dec = 2)
        => new KryptonNumericUpDown {
            Minimum = min, Maximum = max, Value = val,
            DecimalPlaces = dec, Height = 32
        };

    private KryptonDateTimePicker TimeField(int h = 9, int m = 20)
    {
        var d = new KryptonDateTimePicker {
            Format = DateTimePickerFormat.Time,
            ShowUpDown = true, Height = 32
        };
        d.Value = DateTime.Today.AddHours(h).AddMinutes(m);
        return d;
    }

    private KryptonCheckBox Check(string text)
        => new KryptonCheckBox {
            Text = text, AutoSize = true,
            Margin = new Padding(0, 8, 0, 8)
        };

    // ── INDICATOR CONDITIONS ─────────────────────────────────────────────────

    // Creates the group box shell with a tinted action bar + flow panel for rows.
    private KryptonGroupBox MakeIndicatorGroup(int yOffset, out FlowLayoutPanel flow)
    {
        var grp = new KryptonGroupBox {
            Text     = "📈  Indicator Conditions",
            Height   = 120,   // grows by 44px per row added
            Location = new Point(0, yOffset),
            Anchor   = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        // Tinted top action bar ─────────────────────────────────────────────
        var topBar = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = INDBAR };

        var logicLbl = new KryptonLabel {
            Text = "LOGIC:", AutoSize = true,
            Location = new Point(10, 13)
        };
        logicLbl.StateNormal.ShortText.Font   = new Font("Segoe UI", 8f, FontStyle.Bold);
        logicLbl.StateNormal.ShortText.Color1 = LABEL;

        var logicCombo = new KryptonComboBox {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 190, Height = 26,
            Location = new Point(62, 9)
        };
        logicCombo.Items.AddRange(new object[] { "ALL must match (AND)", "ANY must match (OR)" });
        logicCombo.SelectedIndex = 0;

        var addBtn = new KryptonButton {
            Text = "+ Add Condition",
            Width = 138, Height = 28,
            ButtonStyle = ButtonStyle.Standalone
        };
        addBtn.StateCommon.Content.ShortText.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);

        void PositionAdd() =>
            addBtn.Location = new Point(Math.Max(topBar.ClientSize.Width - 148, 264), 8);
        topBar.Resize += (s, e) => PositionAdd();
        // Initial position on first paint (width may be 0 at construction)
        grp.Paint += (s, e) => { if (addBtn.Left < 264) PositionAdd(); };

        topBar.Controls.AddRange(new Control[] { logicLbl, logicCombo, addBtn });

        // Scrollable flow for condition rows ──────────────────────────────
        var innerFlow = new FlowLayoutPanel {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents  = false,
            AutoScroll    = false,
            BackColor     = Color.Transparent,
            Padding       = new Padding(4, 4, 4, 4)
        };

        grp.Panel.Controls.Add(innerFlow);
        grp.Panel.Controls.Add(topBar);
        topBar.BringToFront();

        flow = innerFlow;
        addBtn.Click += (s, e) => AddIndicatorRow(innerFlow, grp);
        return grp;
    }

    // Appends one condition row to the indicator flow and grows the group box.
    private void AddIndicatorRow(FlowLayoutPanel flow, KryptonGroupBox grp)
    {
        var row = new Panel {
            Height    = 40,
            BackColor = Color.FromArgb(250, 252, 255),
            Margin    = new Padding(0, 2, 0, 2)
        };

        // ── Controls ─────────────────────────────────────────────────────
        var indCombo = new KryptonComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        indCombo.Items.AddRange(INDICATORS);
        indCombo.SelectedIndex = 0;

        var periodNum = new KryptonNumericUpDown {
            Minimum = 1, Maximum = 500, Value = 14, DecimalPlaces = 0
        };

        var condCombo = new KryptonComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        condCombo.Items.AddRange(IND_CONDITIONS);
        condCombo.SelectedIndex = 2;   // "Is Above >"

        var valueNum = new KryptonNumericUpDown {
            Minimum = -9999, Maximum = 9999, Value = 30, DecimalPlaces = 2
        };

        var srcCombo = new KryptonComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        srcCombo.Items.AddRange(IND_SOURCES);
        srcCombo.SelectedIndex = 0;    // "Close"

        var logicCombo = new KryptonComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        logicCombo.Items.AddRange(new object[] { "AND", "OR" });
        logicCombo.SelectedIndex = 0;

        var removeBtn = new KryptonButton {
            Text = "×", ButtonStyle = ButtonStyle.LowProfile, Width = 28
        };
        removeBtn.StateCommon.Content.ShortText.Font   = new Font("Segoe UI", 10f, FontStyle.Bold);
        removeBtn.StateCommon.Content.ShortText.Color1 = LOSS;

        row.Controls.AddRange(new Control[] {
            indCombo, periodNum, condCombo, valueNum, srcCombo, logicCombo, removeBtn
        });

        // ── Proportional layout ──────────────────────────────────────────
        // Fixed cols: Period(68) + Value(70) + Logic(62) + Remove(28) + 6 gaps(6) = 264px
        // Flex cols: Indicator, Condition, Source split across remaining width
        void LayoutRow()
        {
            int w = row.ClientSize.Width;
            if (w < 80) return;

            int flex = Math.Max(w - 264, 120);
            int iW   = (int)(flex * 0.40);          // Indicator
            int cW   = (int)(flex * 0.38);          // Condition
            int sW   = Math.Max(flex - iW - cW, 56); // Source

            int cy = (row.Height - 26) / 2;
            int x  = 4;

            indCombo.Location   = new Point(x, cy); indCombo.Size   = new Size(iW, 26); x += iW + 6;
            periodNum.Location  = new Point(x, cy); periodNum.Size  = new Size(68, 26); x += 68 + 6;
            condCombo.Location  = new Point(x, cy); condCombo.Size  = new Size(cW, 26); x += cW + 6;
            valueNum.Location   = new Point(x, cy); valueNum.Size   = new Size(70, 26); x += 70 + 6;
            srcCombo.Location   = new Point(x, cy); srcCombo.Size   = new Size(sW, 26); x += sW + 6;
            logicCombo.Location = new Point(x, cy); logicCombo.Size = new Size(62, 26); x += 62 + 6;
            removeBtn.Location  = new Point(x, cy); removeBtn.Size  = new Size(28, 26);
        }

        row.Resize += (s, e) => LayoutRow();
        row.Paint  += (s, e) => { if (indCombo.Width < 20) LayoutRow(); };

        removeBtn.Click += (s, e) => {
            flow.Controls.Remove(row);
            grp.Height = Math.Max(120, grp.Height - 44);
            row.Dispose();
        };

        void SetRowWidth() =>
            row.Width = Math.Max(flow.ClientSize.Width - flow.Padding.Horizontal - 2, 200);
        flow.Resize += (s, e) => SetRowWidth();

        flow.Controls.Add(row);
        grp.Height += 44;
        SetRowWidth();
        LayoutRow();
    }

    // ── Helper: register width-sync for a control added below the main grid ──
    private void AddWidthSync(System.Windows.Forms.Panel scroll, Control ctrl)
    {
        void Sync() => ctrl.Width = Math.Max(
            scroll.ClientSize.Width - scroll.Padding.Horizontal, 200);
        scroll.Resize += (s, e) => Sync();
        scroll.Paint  += (s, e) => { if (ctrl.Width < 100) Sync(); };
        Sync();
    }

    // ── PAGE FILL METHODS ────────────────────────────────────────────────────

    private void FillGeneralSetup(System.Windows.Forms.Panel s)
    {
        var g = MakeGrid(5);

        var nameWrap = Field("STRATEGY NAME", new KryptonTextBox());
        g.Controls.Add(nameWrap, 0, 0);
        g.SetColumnSpan(nameWrap, 2);

        g.Controls.Add(Field("UNDERLYING SYMBOL",
            Combo("NIFTY", "BANKNIFTY", "FINNIFTY", "SENSEX")), 0, 1);
        g.Controls.Add(Field("SEGMENT",
            Combo("NFO — Options", "NFO — Futures", "NSE — Equity")), 1, 1);

        g.Controls.Add(Field("PRODUCT TYPE",
            Combo("NRML", "MIS", "CNC")), 0, 2);
        g.Controls.Add(Field("EXECUTION RULE",
            Combo("Long + Short", "Long Only", "Short Only")), 1, 2);

        g.Controls.Add(Field("START TIME", TimeField(9,  20)), 0, 3);
        g.Controls.Add(Field("END TIME",   TimeField(15, 15)), 1, 3);

        g.Controls.Add(CheckCell("Carry Forward (Positional)"),    0, 4);
        g.Controls.Add(CheckCell("Enable Strategy Active Status"), 1, 4);

        g.Width = Math.Max(s.ClientSize.Width - s.Padding.Horizontal, 200);
        s.Resize += (ss, ee) =>
            g.Width = Math.Max(((System.Windows.Forms.Panel)ss).ClientSize.Width
                               - ((System.Windows.Forms.Panel)ss).Padding.Horizontal, 200);
        s.Controls.Add(g);
    }

    private void FillEntry(System.Windows.Forms.Panel s)
    {
        // ── Order mechanics (3 rows) ──────────────────────────────────────
        var g = MakeGrid(3);

        g.Controls.Add(Field("ENTRY TYPE",
            Combo("Immediate", "Wait % Up", "Wait Points Up")), 0, 0);
        g.Controls.Add(Field("WAIT VALUE",  Num()), 1, 0);
        g.Controls.Add(Field("ORDER TYPE",  Combo("Market", "Limit", "SL-Limit")), 0, 1);
        g.Controls.Add(Field("BUFFER TYPE", Combo("Points", "Percentage")), 1, 1);

        var chk = CheckCell("First entry only — never re-enter after exit");
        g.Controls.Add(chk, 0, 2);
        g.SetColumnSpan(chk, 2);

        g.Width = Math.Max(s.ClientSize.Width - s.Padding.Horizontal, 200);
        s.Resize += (ss, ee) =>
            g.Width = Math.Max(((System.Windows.Forms.Panel)ss).ClientSize.Width
                               - ((System.Windows.Forms.Panel)ss).Padding.Horizontal, 200);
        s.Controls.Add(g);

        // ── Indicator entry conditions ────────────────────────────────────
        int sepY = 3 * ROW_H + 16;
        var sep  = SectionSep("📈  INDICATOR ENTRY CONDITIONS", sepY);
        AddWidthSync(s, sep);
        s.Controls.Add(sep);

        var grp = MakeIndicatorGroup(sepY + sep.Height + 4, out _);
        AddWidthSync(s, grp);
        s.Controls.Add(grp);
    }

    private void FillExit(System.Windows.Forms.Panel s)
    {
        // ── Order mechanics (2 rows) ──────────────────────────────────────
        var g = MakeGrid(2);

        g.Controls.Add(Field("DAILY SQ-OFF TIME", TimeField(15, 15)), 0, 0);
        g.Controls.Add(Field("EXIT ORDER TYPE",   Combo("Market", "Limit")), 1, 0);

        var chk = CheckCell("Enable daily auto square-off");
        g.Controls.Add(chk, 0, 1);
        g.SetColumnSpan(chk, 2);

        g.Width = Math.Max(s.ClientSize.Width - s.Padding.Horizontal, 200);
        s.Resize += (ss, ee) =>
            g.Width = Math.Max(((System.Windows.Forms.Panel)ss).ClientSize.Width
                               - ((System.Windows.Forms.Panel)ss).Padding.Horizontal, 200);
        s.Controls.Add(g);

        // ── Indicator exit conditions ─────────────────────────────────────
        int sepY = 2 * ROW_H + 16;
        var sep  = SectionSep("📈  INDICATOR EXIT CONDITIONS", sepY);
        AddWidthSync(s, sep);
        s.Controls.Add(sep);

        var grp = MakeIndicatorGroup(sepY + sep.Height + 4, out _);
        AddWidthSync(s, grp);
        s.Controls.Add(grp);
    }

    private void FillRisk(System.Windows.Forms.Panel s)
    {
        var g = MakeGrid(1);

        var tgt = Num(0, 999_999, 10_000);
        tgt.StateCommon.Content.Color1 = PROFIT;
        g.Controls.Add(Field("MTM TARGET VALUE (₹)", tgt), 0, 0);

        var sl = Num(0, 999_999, 5_000);
        sl.StateCommon.Content.Color1 = LOSS;
        g.Controls.Add(Field("MTM STOPLOSS VALUE (₹)", sl), 1, 0);

        g.Width = Math.Max(s.ClientSize.Width - s.Padding.Horizontal, 200);
        s.Resize += (ss, ee) =>
            g.Width = Math.Max(((System.Windows.Forms.Panel)ss).ClientSize.Width
                               - ((System.Windows.Forms.Panel)ss).Padding.Horizontal, 200);
        s.Controls.Add(g);
    }

    private void FillReEntry(System.Windows.Forms.Panel s)
    {
        var g = MakeGrid(1);

        g.Controls.Add(Field("RE-ENTRY TYPE",  Combo("Immediate", "At Cost")), 0, 0);
        g.Controls.Add(Field("MAX RE-ENTRIES", Num(0, 10, 3, 0)), 1, 0);

        g.Width = Math.Max(s.ClientSize.Width - s.Padding.Horizontal, 200);
        s.Resize += (ss, ee) =>
            g.Width = Math.Max(((System.Windows.Forms.Panel)ss).ClientSize.Width
                               - ((System.Windows.Forms.Panel)ss).Padding.Horizontal, 200);
        s.Controls.Add(g);
    }

    // ── MULTI-LEG BUILDER ────────────────────────────────────────────────────

    private void FillMultiLegPage(System.Windows.Forms.Panel s)
    {
        s.Padding = new Padding(0);

        var main = new KryptonPanel { Dock = DockStyle.Fill };
        main.StateCommon.Color1 = PAGEBG;

        var topBar = new KryptonPanel {
            Dock = DockStyle.Top, Height = 50,
            Padding = new Padding(16, 10, 16, 10)
        };
        var addBtn = new KryptonButton {
            Text = "+ Add Leg", Dock = DockStyle.Left,
            Width = 130, ButtonStyle = ButtonStyle.Standalone
        };
        addBtn.StateCommon.Content.ShortText.Font =
            new Font("Segoe UI", 9f, FontStyle.Bold);
        topBar.Controls.Add(addBtn);
        main.Controls.Add(topBar);

        _legContainer = new FlowLayoutPanel {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(16, 8, 16, 60),
            BackColor = PAGEBG
        };
        main.Controls.Add(_legContainer);
        s.Controls.Add(main);

        addBtn.Click += (se, ev) => AddLegCard();
        AddLegCard();
    }

    private void AddLegCard()
    {
        _legCount++;
        int num = _legCount;

        var card = new KryptonGroupBox {
            Text = $"Leg {num}",
            Height = 200,
            Dock = DockStyle.None,
            Margin = new Padding(0, 4, 0, 12),
            GroupBackStyle = PaletteBackStyle.PanelClient
        };

        card.Width = Math.Max(_legContainer!.ClientSize.Width - 34, 200);
        _legContainer.Resize += (s, e) =>
            card.Width = Math.Max(_legContainer.ClientSize.Width - 34, 200);

        var table = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 6, RowCount = 4,
            Padding = new Padding(8, 6, 8, 6),
            BackColor = Color.Transparent
        };
        for (int i = 0; i < 6; i++)
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.667f));

        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));

        void L(string txt, int c, int r)
        {
            var lbl = new KryptonLabel {
                Text = txt, Dock = DockStyle.Fill, AutoSize = false
            };
            lbl.StateNormal.ShortText.Font   = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            lbl.StateNormal.ShortText.Color1 = LABEL;
            lbl.StateNormal.ShortText.TextV  = PaletteRelativeAlign.Far;
            table.Controls.Add(lbl, c, r);
        }
        void C(Control ctrl, int c, int r)
        {
            ctrl.Dock   = DockStyle.Top;
            ctrl.Height = 28;
            table.Controls.Add(ctrl, c, r);
        }

        L("B/S", 0, 0); L("TYPE", 1, 0); L("OPTION", 2, 0);
        L("STRIKE", 3, 0); L("LOTS", 4, 0); L("EXPIRY", 5, 0);

        C(Combo("BUY", "SELL"), 0, 1);
        C(Combo("NRML", "MIS"), 1, 1);
        C(Combo("CE", "PE", "FUT"), 2, 1);
        C(Combo("ATM", "ATM+1", "ATM+2", "ATM+3", "ATM-1", "ATM-2", "ATM-3"), 3, 1);
        C(Num(1, 500, 1, 0), 4, 1);
        C(Combo("Weekly", "Next", "Monthly"), 5, 1);

        L("TARGET %", 0, 2); L("SL %", 1, 2);
        L("TRAIL X", 2, 2); L("TRAIL Y", 3, 2); L("ON HIT", 4, 2);

        C(Num(0, 1000, 50, 1), 0, 3);
        C(Num(0, 1000, 100, 1), 1, 3);
        C(Num(0, 9999), 2, 3);
        C(Num(0, 9999), 3, 3);
        C(Combo("SqOff Leg", "SqOff ALL"), 4, 3);

        card.Panel.Controls.Add(table);
        _legContainer.Controls.Add(card);
        _legContainer.ScrollControlIntoView(card);
    }
}
}
