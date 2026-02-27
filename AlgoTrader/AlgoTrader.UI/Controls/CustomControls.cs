using AlgoTrader.UI.Theme;

namespace AlgoTrader.UI.Controls;

/// <summary>Price display label — flashes green/red on change with arrow indicator.</summary>
public class PriceLabel : Control
{
    private decimal _price;
    private decimal _previousPrice;
    private decimal _change;
    private Color _flashColor = Color.Transparent;
    private System.Windows.Forms.Timer _flashTimer;
    private int _flashAlpha = 0;

    public bool ShowChange { get; set; } = true;
    public bool ShowArrow { get; set; } = true;
    public string Prefix { get; set; } = "₹";

    public decimal Price
    {
        get => _price;
        set
        {
            _previousPrice = _price;
            _price = value;
            _change = value - _previousPrice;
            if (_previousPrice != 0 && _change != 0)
                TriggerFlash(_change > 0 ? ThemeManager.Positive : ThemeManager.Negative);
            Invalidate();
        }
    }

    public PriceLabel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        Height = 40;
        Width = 180;
        Font = AppFonts.Price;

        _flashTimer = new System.Windows.Forms.Timer { Interval = 30 };
        _flashTimer.Tick += (s, e) =>
        {
            _flashAlpha = Math.Max(0, _flashAlpha - 12);
            if (_flashAlpha <= 0) _flashTimer.Stop();
            Invalidate();
        };
    }

    private void TriggerFlash(Color color)
    {
        _flashColor = color;
        _flashAlpha = 100;
        _flashTimer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // Flash background
        if (_flashAlpha > 0)
        {
            using var flashBrush = new SolidBrush(Color.FromArgb(_flashAlpha, _flashColor));
            g.FillRectangle(flashBrush, ClientRectangle);
        }

        // Price text
        var priceText = $"{Prefix}{IndianFormat.FormatNumber(_price)}";
        var priceColor = _change >= 0 ? ThemeManager.Positive : ThemeManager.Negative;
        using var priceBrush = new SolidBrush(priceColor);
        g.DrawString(priceText, AppFonts.Price, priceBrush, 2, 2);

        // Change + arrow
        if (ShowChange && _previousPrice != 0)
        {
            var arrow = _change >= 0 ? "▲" : "▼";
            var changeText = ShowArrow
                ? $"{arrow} {IndianFormat.FormatChange(_change)}"
                : IndianFormat.FormatChange(_change);
            using var changeBrush = new SolidBrush(Color.FromArgb(180, priceColor));
            g.DrawString(changeText, AppFonts.Tiny, changeBrush, 4, Height - 16);
        }
    }
}

/// <summary>Pulsing connection status indicator — green/red/amber dot with label.</summary>
public class StatusIndicator : Control
{
    public enum ConnectionState { Connected, Disconnected, Connecting }

    private ConnectionState _state = ConnectionState.Disconnected;
    private System.Windows.Forms.Timer _pulseTimer;
    private float _pulsePhase = 0f;

    public ConnectionState State
    {
        get => _state;
        set { _state = value; Invalidate(); }
    }

    public string StatusText { get; set; } = "";

    public StatusIndicator()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        Height = 24;
        Width = 160;

        _pulseTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _pulseTimer.Tick += (s, e) =>
        {
            _pulsePhase += 0.15f;
            if (_pulsePhase > 2 * MathF.PI) _pulsePhase -= 2 * MathF.PI;
            Invalidate();
        };
        _pulseTimer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // Determine dot color
        var baseColor = _state switch
        {
            ConnectionState.Connected => ThemeManager.Positive,
            ConnectionState.Connecting => ThemeManager.Warning,
            _ => ThemeManager.Negative
        };

        // Pulsing opacity (connected/connecting pulse, disconnected stays solid)
        int alpha = _state == ConnectionState.Disconnected
            ? 255
            : (int)(180 + 75 * Math.Sin(_pulsePhase));

        // Draw dot
        var dotSize = 10;
        var dotY = (Height - dotSize) / 2;
        using var dotBrush = new SolidBrush(Color.FromArgb(alpha, baseColor));
        g.FillEllipse(dotBrush, 4, dotY, dotSize, dotSize);

        // Glow effect
        if (_state == ConnectionState.Connected)
        {
            var glowAlpha = (int)(30 + 20 * Math.Sin(_pulsePhase));
            using var glowBrush = new SolidBrush(Color.FromArgb(glowAlpha, baseColor));
            g.FillEllipse(glowBrush, 1, dotY - 3, dotSize + 6, dotSize + 6);
        }

        // Label text
        var text = string.IsNullOrEmpty(StatusText) ? _state.ToString() : StatusText;
        using var textBrush = new SolidBrush(ThemeManager.SubText);
        g.DrawString(text, AppFonts.Tiny, textBrush, dotSize + 10, (Height - 14) / 2);
    }
}

/// <summary>
/// Large MTM hero display — 28pt number with smooth green↔red transition,
/// sub-labels for realized/unrealized P&L, and background glow on threshold.
/// </summary>
public class MTMDisplayPanel : Control
{
    private decimal _totalMTM;
    private decimal _realizedPnL;
    private decimal _unrealizedPnL;
    private decimal _brokerage;
    private decimal _glowThreshold = 5000m;
    private float _glowPhase = 0f;
    private System.Windows.Forms.Timer _animTimer;

    public decimal TotalMTM
    {
        get => _totalMTM;
        set { _totalMTM = value; Invalidate(); }
    }

    public decimal RealizedPnL { get => _realizedPnL; set { _realizedPnL = value; Invalidate(); } }
    public decimal UnrealizedPnL { get => _unrealizedPnL; set { _unrealizedPnL = value; Invalidate(); } }
    public decimal Brokerage { get => _brokerage; set { _brokerage = value; Invalidate(); } }
    public decimal GlowThreshold { get => _glowThreshold; set => _glowThreshold = value; }

    public MTMDisplayPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        Height = 120;
        Width = 320;

        _animTimer = new System.Windows.Forms.Timer { Interval = 40 };
        _animTimer.Tick += (s, e) =>
        {
            _glowPhase += 0.08f;
            if (_glowPhase > 2 * MathF.PI) _glowPhase -= 2 * MathF.PI;
            if (Math.Abs(_totalMTM) > _glowThreshold) Invalidate();
        };
        _animTimer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // Background
        using var bgBrush = new SolidBrush(ThemeManager.Surface);
        g.FillRectangle(bgBrush, ClientRectangle);

        // Glow effect when above threshold
        if (Math.Abs(_totalMTM) > _glowThreshold)
        {
            var glowColor = _totalMTM >= 0 ? ThemeManager.Positive : ThemeManager.Negative;
            var glowAlpha = (int)(15 + 10 * Math.Sin(_glowPhase));
            using var glowBrush = new SolidBrush(Color.FromArgb(glowAlpha, glowColor));
            g.FillRectangle(glowBrush, ClientRectangle);
        }

        // Border
        using var borderPen = new Pen(ThemeManager.Border);
        g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

        // Main MTM number
        var mtmColor = _totalMTM >= 0 ? ThemeManager.Positive : ThemeManager.Negative;
        var mtmText = IndianFormat.FormatCurrency(_totalMTM);
        using var mtmBrush = new SolidBrush(mtmColor);
        g.DrawString(mtmText, AppFonts.MTMHero, mtmBrush, 12, 8);

        // Sub-labels
        var y = 60;
        DrawSubLabel(g, "Realized:", IndianFormat.FormatCurrency(_realizedPnL), _realizedPnL >= 0, 12, y);
        DrawSubLabel(g, "Unrealized:", IndianFormat.FormatCurrency(_unrealizedPnL), _unrealizedPnL >= 0, 12, y + 18);
        DrawSubLabel(g, "Brokerage:", IndianFormat.FormatCurrency(_brokerage), false, 12, y + 36);
    }

    private void DrawSubLabel(Graphics g, string label, string value, bool positive, int x, int y)
    {
        using var labelBrush = new SolidBrush(ThemeManager.SubText);
        using var valueBrush = new SolidBrush(positive ? ThemeManager.Positive : ThemeManager.Negative);
        g.DrawString(label, AppFonts.Tiny, labelBrush, x, y);
        g.DrawString(value, AppFonts.BodyBold, valueBrush, x + 70, y - 1);
    }
}

/// <summary>Connection diagnostics panel — feed status, tick rate, token expiry, latency.</summary>
public class ConnectionDiagnosticsPanel : Control
{
    private string _feedStatus = "Disconnected";
    private Color _feedColor = ThemeManager.Negative;
    private DateTime _lastTickTime;
    private double _ticksPerSec;
    private DateTime? _tokenExpiry;
    private DateTime? _lastOrderTime;
    private int _reconnectAttempts;

    public void UpdateDiagnostics(string feedStatus, Color feedColor, DateTime lastTick,
        double tps, DateTime? tokenExpiry, DateTime? lastOrder, int reconnects)
    {
        _feedStatus = feedStatus;
        _feedColor = feedColor;
        _lastTickTime = lastTick;
        _ticksPerSec = tps;
        _tokenExpiry = tokenExpiry;
        _lastOrderTime = lastOrder;
        _reconnectAttempts = reconnects;
        Invalidate();
    }

    public ConnectionDiagnosticsPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        Height = 130;
        Width = 260;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // Background card
        using var bgBrush = new SolidBrush(ThemeManager.Card);
        g.FillRectangle(bgBrush, ClientRectangle);
        using var borderPen = new Pen(ThemeManager.Border);
        g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

        // Title
        using var titleBrush = new SolidBrush(ThemeManager.Accent);
        g.DrawString("📡 CONNECTION", AppFonts.BodyBold, titleBrush, 8, 5);

        var y = 26;
        var lineH = 17;
        DrawRow(g, "Feed:", _feedStatus, _feedColor, 8, y); y += lineH;
        DrawRow(g, "Last Tick:", _lastTickTime == default ? "--" : _lastTickTime.ToString("HH:mm:ss.fff"), ThemeManager.Text, 8, y); y += lineH;
        DrawRow(g, "Ticks/sec:", $"{_ticksPerSec:F1}", _ticksPerSec > 0 ? ThemeManager.Positive : ThemeManager.SubText, 8, y); y += lineH;

        var expiryText = _tokenExpiry.HasValue
            ? $"{(_tokenExpiry.Value - DateTime.UtcNow):hh\\:mm\\:ss}"
            : "--";
        var expiryColor = _tokenExpiry.HasValue && (_tokenExpiry.Value - DateTime.UtcNow).TotalMinutes < 30
            ? ThemeManager.Warning : ThemeManager.Text;
        DrawRow(g, "Token TTL:", expiryText, expiryColor, 8, y); y += lineH;

        DrawRow(g, "Last Order:", _lastOrderTime?.ToString("HH:mm:ss") ?? "--", ThemeManager.SubText, 8, y); y += lineH;
        DrawRow(g, "Reconnects:", _reconnectAttempts.ToString(), _reconnectAttempts > 0 ? ThemeManager.Warning : ThemeManager.SubText, 8, y);
    }

    private void DrawRow(Graphics g, string label, string value, Color valueColor, int x, int y)
    {
        using var lblBrush = new SolidBrush(ThemeManager.SubText);
        using var valBrush = new SolidBrush(valueColor);
        g.DrawString(label, AppFonts.Tiny, lblBrush, x, y);
        g.DrawString(value, AppFonts.Body, valBrush, x + 80, y - 1);
    }
}
