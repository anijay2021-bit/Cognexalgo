using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace AlgoTrader.UI.Theme;

/// <summary>Cognex Pro theme manager — Bloomberg-grade dark palette for financial UI.</summary>
public static class ThemeManager
{
    // ─── Dark Theme (default) ───
    public static readonly Color Background   = Color.FromArgb(13, 17, 23);     // #0D1117
    public static readonly Color Surface      = Color.FromArgb(22, 27, 34);     // #161B22
    public static readonly Color Card         = Color.FromArgb(28, 33, 40);     // #1C2128
    public static readonly Color CardHover    = Color.FromArgb(36, 41, 50);     // #242932
    public static readonly Color Border       = Color.FromArgb(48, 54, 61);     // #30363D
    public static readonly Color BorderLight  = Color.FromArgb(60, 66, 75);     // #3C424B
    public static readonly Color Text         = Color.FromArgb(230, 237, 243);  // #E6EDF3
    public static readonly Color SubText      = Color.FromArgb(139, 148, 158);  // #8B949E
    public static readonly Color Muted        = Color.FromArgb(80, 86, 96);     // #505660
    public static readonly Color Positive     = Color.FromArgb(63, 185, 80);    // #3FB950
    public static readonly Color PositiveDim  = Color.FromArgb(30, 80, 40);
    public static readonly Color Negative     = Color.FromArgb(248, 81, 73);    // #F85149
    public static readonly Color NegativeDim  = Color.FromArgb(80, 30, 30);
    public static readonly Color Warning      = Color.FromArgb(210, 153, 34);   // #D29922
    public static readonly Color Accent       = Color.FromArgb(88, 166, 255);   // #58A6FF
    public static readonly Color AccentDim    = Color.FromArgb(25, 50, 80);
    public static readonly Color Gold         = Color.FromArgb(255, 215, 0);    // #FFD700
    public static readonly Color Purple       = Color.FromArgb(188, 140, 255);  // #BC8CFF
    public static readonly Color ATMHighlight = Color.FromArgb(60, 55, 20);
    public static readonly Color ITMTint      = Color.FromArgb(20, 35, 55);

    // ─── TopBar ───
    public static readonly Color TopBar       = Color.FromArgb(10, 14, 20);
    public static readonly Color TopBarBorder = Color.FromArgb(30, 50, 80);

    // ─── Grid ───
    public static readonly Color GridRow1     = Color.FromArgb(22, 27, 34);
    public static readonly Color GridRow2     = Color.FromArgb(28, 33, 40);
    public static readonly Color GridHeader   = Color.FromArgb(13, 17, 23);
    public static readonly Color GridHeaderFg = Color.FromArgb(180, 200, 255);
    public static readonly Color GridSelect   = Color.FromArgb(50, 60, 85);

    /// <summary>Recursively applies the dark theme to a Form and all children.</summary>
    public static void ApplyTheme(Control control)
    {
        control.BackColor = Background;
        control.ForeColor = Text;

        foreach (Control child in control.Controls)
        {
            if (child is DataGridView dgv)
                ApplyGridTheme(dgv);
            else if (child is TabControl tab)
            {
                tab.BackColor = Background;
                tab.ForeColor = Text;
                foreach (TabPage page in tab.TabPages)
                {
                    page.BackColor = Background;
                    page.ForeColor = Text;
                }
            }
            else if (child is ToolStrip ts)
                ts.BackColor = Surface;
            else if (child is StatusStrip ss)
                ss.BackColor = TopBar;
            else if (child is TextBox tb)
            {
                tb.BackColor = Card;
                tb.ForeColor = Text;
                tb.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (child is ComboBox cb)
            {
                cb.BackColor = Card;
                cb.ForeColor = Text;
            }
            else if (child is Button btn)
            {
                btn.FlatStyle = FlatStyle.Flat;
                btn.BackColor = Card;
                btn.ForeColor = Text;
                btn.FlatAppearance.BorderColor = Border;
            }

            ApplyTheme(child);
        }
    }

    public static void ApplyGridTheme(DataGridView dgv)
    {
        dgv.BackgroundColor = Background;
        dgv.GridColor = Border;
        dgv.BorderStyle = BorderStyle.None;
        dgv.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        dgv.EnableHeadersVisualStyles = false;
        dgv.RowHeadersVisible = false;
        dgv.AllowUserToAddRows = false;
        dgv.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        dgv.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = GridHeader,
            ForeColor = GridHeaderFg,
            Font = AppFonts.Body,
            SelectionBackColor = GridHeader,
            Padding = new Padding(4),
        };
        dgv.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = GridRow1,
            ForeColor = Text,
            SelectionBackColor = GridSelect,
            SelectionForeColor = Text,
            Font = AppFonts.Body,
        };
        dgv.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = GridRow2,
            ForeColor = Text,
            SelectionBackColor = GridSelect,
        };
    }
}

/// <summary>Cognex Pro typography constants.</summary>
public static class AppFonts
{
    public static readonly Font Header    = new("Segoe UI", 13f, FontStyle.Bold);
    public static readonly Font SubHeader = new("Segoe UI", 11f, FontStyle.Regular);
    public static readonly Font Body      = new("Segoe UI", 9.5f, FontStyle.Regular);
    public static readonly Font BodyBold  = new("Segoe UI Semibold", 9.5f, FontStyle.Regular);
    public static readonly Font Mono      = new("Cascadia Code", 9f, FontStyle.Regular);
    public static readonly Font Tiny      = new("Segoe UI", 8f, FontStyle.Regular);
    public static readonly Font Price     = new("Cascadia Mono", 10f, FontStyle.Bold);
    public static readonly Font PriceLg   = new("Cascadia Mono", 14f, FontStyle.Bold);
    public static readonly Font MTMHero   = new("Cascadia Mono", 28f, FontStyle.Bold);
}

/// <summary>Indian number formatting utilities.</summary>
public static class IndianFormat
{
    private static readonly CultureInfo _indianCulture = new("en-IN");

    public static string FormatCurrency(decimal value) => $"₹{value.ToString("N2", _indianCulture)}";
    public static string FormatNumber(decimal value) => value.ToString("N2", _indianCulture);
    public static string FormatChange(decimal value) => value >= 0 ? $"+{FormatNumber(value)}" : FormatNumber(value);
    public static string FormatPercent(decimal value) => value >= 0 ? $"+{value:F2}%" : $"{value:F2}%";
}
