using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AlgoTrader.UI.Theme
{
    // ── Color Palette (extracted exactly from HTML) ──────────────────────
    public static class ArcticColors
    {
        // Backgrounds
        public static Color AppBackground     = ColorFromHex("#D8E8F8"); // body bg
        public static Color WindowBg          = ColorFromHex("#EDF4FC"); // .window bg
        public static Color ContentBg         = ColorFromHex("#F7FBFF"); // .content
        public static Color CardBg            = ColorFromHex("#FFFFFF"); // cards
        public static Color SidebarBg         = ColorFromHex("#EDF4FC"); // sidebar
        public static Color TabBarBg          = ColorFromHex("#F0F8FF"); // tabs bar
        public static Color MenuBarBg         = ColorFromHex("#FFFFFF"); // menubar
        public static Color BottomBarBg1      = ColorFromHex("#E8F2FC"); // bottombar gradient start
        public static Color BottomBarBg2      = ColorFromHex("#D8EAF8"); // bottombar gradient end

        // Header gradient (blue band)
        public static Color HeaderBg1         = ColorFromHex("#0E4C92");
        public static Color HeaderBg2         = ColorFromHex("#1565C0");
        public static Color HeaderBg3         = ColorFromHex("#1976D2");

        // Borders
        public static Color BorderLight       = ColorFromHex("#C8DCF0");
        public static Color BorderMedium      = ColorFromHex("#B8CDE0");
        public static Color BorderStrong      = ColorFromHex("#C0D4E8");
        public static Color TabBorder         = ColorFromHex("#B8D8F0");
        public static Color CardBorder        = ColorFromHex("#C8E0F4");

        // Text
        public static Color TextPrimary       = ColorFromHex("#0D3060");
        public static Color TextNav           = ColorFromHex("#1A4070");
        public static Color TextLabel         = ColorFromHex("#6090B8"); // kpi-label
        public static Color TextMuted         = ColorFromHex("#90B0D0");
        public static Color TextAccent        = ColorFromHex("#1565C0");
        public static Color TextHeader        = ColorFromHex("#2A5080"); // titlebar

        // Accent / Interactive
        public static Color AccentBlue        = ColorFromHex("#1565C0");
        public static Color AccentBlueLt      = ColorFromHex("#42A5F5");
        public static Color HoverBg           = ColorFromHex("#E8F2FC");
        public static Color ActiveBg          = ColorFromHex("#D0E8F8");
        public static Color ActiveBorder      = ColorFromHex("#80B8E0");
        public static Color ActiveTabBg       = ColorFromHex("#FFFFFF");
        public static Color SelectedCardLeft  = ColorFromHex("#1565C0");

        // Status
        public static Color ProfitGreen       = ColorFromHex("#1B6E38");
        public static Color ProfitGreenLt     = ColorFromHex("#22BB66");
        public static Color LossRed           = ColorFromHex("#DD4444");
        public static Color WarningAmber      = ColorFromHex("#D4A017");

        // Header overlays
        public static Color HeaderText        = Color.White;
        public static Color HeaderSubText     = ColorFromHex("#BBDEFB");
        public static Color HeaderIndexName   = ColorFromHex("#90CAF9");
        public static Color HeaderPillBg      = Color.FromArgb(30, 255, 255, 255);
        public static Color HeaderPillBorder  = Color.FromArgb(50, 255, 255, 255);
        public static Color LiveGreen         = ColorFromHex("#69F0AE");
        public static Color LiveRed           = ColorFromHex("#FF5252");
        public static Color LogoGold          = ColorFromHex("#FFD54F");

        // Chart
        public static Color ChartLine        = ColorFromHex("#1565C0");
        public static Color ChartFillTop     = Color.FromArgb(50, 21, 101, 192);
        public static Color ChartFillBot     = Color.FromArgb(5,  21, 101, 192);
        public static Color ChartGrid        = ColorFromHex("#EEF4FC");
        public static Color ChartGridDash    = ColorFromHex("#C8DCF0");
        public static Color ChartDotGreen    = ColorFromHex("#22AA55");
        public static Color ChartDotRed      = ColorFromHex("#DD4444");

        // Badge backgrounds
        public static Color BadgeEntryBg     = ColorFromHex("#E8F5E9");
        public static Color BadgeEntryText   = ColorFromHex("#2E7D32");
        public static Color BadgeExitBg      = ColorFromHex("#FFEBEE");
        public static Color BadgeExitText    = ColorFromHex("#C62828");
        public static Color BadgeSlBg        = ColorFromHex("#FFF3E0");
        public static Color BadgeSlText      = ColorFromHex("#E65100");

        public static Color ColorFromHex(string hex)
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
            {
                return Color.FromArgb(
                    Convert.ToInt32(hex.Substring(0, 2), 16),
                    Convert.ToInt32(hex.Substring(2, 2), 16),
                    Convert.ToInt32(hex.Substring(4, 2), 16));
            }
            return Color.Black;
        }
    }

    // ── Font Definitions ─────────────────────────────────────────────────
    public static class ArcticFonts
    {
        public static Font Logo       = new Font("Segoe UI", 11f,  FontStyle.Bold);
        public static Font HeaderIdx  = new Font("Segoe UI", 9f,   FontStyle.Bold);
        public static Font HeaderMono = new Font("Courier New", 10f, FontStyle.Bold);
        public static Font Title      = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        public static Font MenuBtn    = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        public static Font TabActive  = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        public static Font TabNormal  = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        public static Font KpiLabel   = new Font("Segoe UI", 7f,   FontStyle.Bold);
        public static Font KpiValue   = new Font("Courier New", 16f, FontStyle.Bold);
        public static Font KpiSub     = new Font("Segoe UI", 7f,   FontStyle.Regular);
        public static Font KpiTag     = new Font("Segoe UI", 7f,   FontStyle.Bold);
        public static Font AccName    = new Font("Segoe UI", 8f,   FontStyle.Bold);
        public static Font AccBroker  = new Font("Segoe UI", 7f,   FontStyle.Regular);
        public static Font AccMTM     = new Font("Courier New", 9f, FontStyle.Bold);
        public static Font SectionLbl = new Font("Segoe UI", 7f,   FontStyle.Bold);
        public static Font ChartTitle = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        public static Font ChartSub   = new Font("Segoe UI", 7f,   FontStyle.Regular);
        public static Font AlertText  = new Font("Segoe UI", 8f,   FontStyle.Regular);
        public static Font AlertTime  = new Font("Courier New", 7f, FontStyle.Regular);
        public static Font BottomBar  = new Font("Segoe UI", 7.5f, FontStyle.Regular);
        public static Font BottomMono = new Font("Courier New", 8f, FontStyle.Bold);
        public static Font BottomMTM  = new Font("Courier New", 9f, FontStyle.Bold);
        public static Font TFBtn      = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        public static Font Body       = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        public static Font BodyBold   = new Font("Segoe UI Semibold", 9.5f, FontStyle.Regular);
    }

    // ── Drawing Helpers ──────────────────────────────────────────────────
    public static class ArcticDraw
    {
        // Rounded rectangle path
        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d <= 0) d = 1;

            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // Draw card with top gradient bar + shadow
        public static void DrawCard(Graphics g, Rectangle r, int radius = 10)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Shadow
            using var shadowBrush = new SolidBrush(Color.FromArgb(18, 20, 80, 160));
            g.FillRectangle(shadowBrush, r.X + 2, r.Y + 4, r.Width - 2, r.Height - 2);

            // Card fill
            using var path = RoundRect(r, radius);
            using var fillBrush = new SolidBrush(ArcticColors.CardBg);
            g.FillPath(fillBrush, path);

            // Border
            using var pen = new Pen(ArcticColors.CardBorder);
            g.DrawPath(pen, path);

            // Top accent bar (blue gradient, 3px)
            var barRect = new Rectangle(r.X + radius, r.Y, r.Width - radius * 2, 3);
            if (barRect.Width > 0)
            {
                using var barBrush = new LinearGradientBrush(
                    barRect, ArcticColors.AccentBlue, ArcticColors.AccentBlueLt, 0f);
                g.FillRectangle(barBrush, barRect);
            }
        }

        // Draw blue header band
        public static void DrawHeader(Graphics g, Rectangle r)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using var brush = new LinearGradientBrush(r,
                ArcticColors.HeaderBg1, ArcticColors.HeaderBg3, 35f);
            g.FillRectangle(brush, r);
        }

        // Draw bottom bar
        public static void DrawBottomBar(Graphics g, Rectangle r)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using var brush = new LinearGradientBrush(r,
                ArcticColors.BottomBarBg1, ArcticColors.BottomBarBg2, 90f);
            g.FillRectangle(brush, r);
            using var pen = new Pen(ArcticColors.BorderLight);
            g.DrawLine(pen, r.Left, r.Top, r.Right, r.Top);
        }

        // Pulsing dot (call from timer, alpha oscillates)
        public static void DrawPulsingDot(Graphics g, Point center, int radius,
            Color color, float alpha = 1f)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Glow
            int glowR = radius + 4;
            using var glowBrush = new SolidBrush(Color.FromArgb((int)(60 * alpha),
                color.R, color.G, color.B));
            g.FillEllipse(glowBrush,
                center.X - glowR, center.Y - glowR, glowR * 2, glowR * 2);
            // Core
            using var coreBrush = new SolidBrush(Color.FromArgb((int)(255 * alpha),
                color.R, color.G, color.B));
            g.FillEllipse(coreBrush,
                center.X - radius, center.Y - radius, radius * 2, radius * 2);
        }

        // Draw index pill (header)
        public static void DrawIndexPill(Graphics g, Rectangle r,
            string name, string value, string change, bool isUp)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundRect(r, 5);
            using var fillBrush = new SolidBrush(ArcticColors.HeaderPillBg);
            g.FillPath(fillBrush, path);
            using var borderPen = new Pen(ArcticColors.HeaderPillBorder);
            g.DrawPath(borderPen, path);

            int y = r.Top + 5;
            // Index name
            using var nameBrush = new SolidBrush(ArcticColors.HeaderIndexName);
            g.DrawString(name, ArcticFonts.HeaderIdx, nameBrush, r.Left + 8, y);
            y += 14;
            // Value
            using var valBrush = new SolidBrush(Color.White);
            g.DrawString(value, ArcticFonts.HeaderMono, valBrush, r.Left + 8, y);
            y += 14;
            // Change
            var chgColor = isUp ? ArcticColors.LiveGreen : Color.FromArgb(255, 138, 128);
            using var chgBrush = new SolidBrush(chgColor);
            g.DrawString((isUp ? "▲" : "▼") + change, ArcticFonts.KpiLabel, chgBrush, r.Left + 8, y);
        }

        // Draw pill badge (alert rows)
        public static void DrawBadge(Graphics g, Rectangle r,
            string text, Color bg, Color fg, int radius = 8)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundRect(r, radius);
            using var fillBrush = new SolidBrush(bg);
            g.FillPath(fillBrush, path);
            using var textBrush = new SolidBrush(fg);
            var fmt = new StringFormat {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString(text, ArcticFonts.KpiTag, textBrush, r, fmt);
        }

        // Draw equity curve chart
        public static void DrawEquityCurve(Graphics g, Rectangle r,
            List<float> values, float minVal, float maxVal)
        {
            if (values == null || values.Count < 2) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Background
            using var bgBrush = new SolidBrush(Color.FromArgb(250, 252, 255));
            g.FillRectangle(bgBrush, r);

            float range = maxVal - minVal;
            if (range == 0) range = 1;

            // Grid lines
            for (int i = 1; i <= 3; i++)
            {
                int y = r.Top + (r.Height * i / 4);
                bool isDash = (i == 2);
                using var gridPen = isDash
                    ? new Pen(ArcticColors.ChartGridDash) { DashStyle = DashStyle.Dash }
                    : new Pen(ArcticColors.ChartGrid);
                g.DrawLine(gridPen, r.Left, y, r.Right, y);
            }

            // Build points
            var points = new PointF[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                float x = r.Left + (float)i / (values.Count - 1) * r.Width;
                float y = r.Bottom - (values[i] - minVal) / range * r.Height;
                points[i] = new PointF(x, y);
            }

            // Fill area under curve
            var fillPoints = new PointF[points.Length + 2];
            fillPoints[0] = new PointF(r.Left, r.Bottom);
            Array.Copy(points, 0, fillPoints, 1, points.Length);
            fillPoints[fillPoints.Length - 1] = new PointF(r.Right, r.Bottom);

            if (r.Width > 0 && r.Height > 0)
            {
                using var fillBrush = new LinearGradientBrush(r,
                    ArcticColors.ChartFillTop, ArcticColors.ChartFillBot, 90f);
                g.FillPolygon(fillBrush, fillPoints);
            }

            // Curve line
            using var linePen = new Pen(ArcticColors.ChartLine, 2.5f);
            linePen.LineJoin = LineJoin.Round;
            g.DrawCurve(linePen, points, 0.4f);
        }
    }
}
