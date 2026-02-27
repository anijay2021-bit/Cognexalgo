 using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using AlgoTrader.UI.Theme;

namespace AlgoTrader.UI.Controls
{
    public class ArcticHeaderPanel : Panel
    {
        private System.Windows.Forms.Timer _pulseTimer;
        private float _pulseAlpha = 1f;
        private bool _pulseDir = false;
        private bool _isLive = true;

        // Data properties
        public string NiftyLTP  { get; set; } = "24,350.50";
        public string NiftyChg  { get; set; } = "+0.19%";
        public bool   NiftyUp   { get; set; } = true;
        public string BnLTP     { get; set; } = "52,180.25";
        public string BnChg     { get; set; } = "-0.31%";
        public bool   BnUp      { get; set; } = false;
        public string FinLTP    { get; set; } = "23,890.00";
        public string FinChg    { get; set; } = "+0.07%";
        public bool   FinUp     { get; set; } = true;
        public string TicksPerSec { get; set; } = "142 tps";
        public bool   IsLive    { get => _isLive; set { _isLive = value; Invalidate(); } }

        public ArcticHeaderPanel()
        {
            Height = 48;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint, true);

            _pulseTimer = new System.Windows.Forms.Timer { Interval = 40 };
            _pulseTimer.Tick += (s, e) => {
                _pulseAlpha += _pulseDir ? -0.04f : 0.04f;
                if (_pulseAlpha <= 0.3f) { _pulseAlpha = 0.3f; _pulseDir = false; }
                if (_pulseAlpha >= 1f)   { _pulseAlpha = 1f;   _pulseDir = true;  }
                Invalidate();
            };
            _pulseTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Background gradient
            ArcticDraw.DrawHeader(g, ClientRectangle);

            int x = 16;

            // Logo
            using var logoBrush = new SolidBrush(ArcticColors.LogoGold);
            g.DrawString("⚡ COGNEX ALGO", ArcticFonts.Logo, logoBrush, x, 14);
            x += 160;

            // Index pills
            DrawIndexPill(g, ref x, "NIFTY",  NiftyLTP, NiftyChg, NiftyUp);
            DrawIndexPill(g, ref x, "BNIFTY", BnLTP,    BnChg,    BnUp);
            DrawIndexPill(g, ref x, "FINNFT", FinLTP,   FinChg,   FinUp);

            // Right side — feed pill + time
            string timeStr = DateTime.Now.ToString("HH:mm:ss") + " IST";
            var timeSz = g.MeasureString(timeStr, ArcticFonts.HeaderMono);
            int rx = Width - (int)timeSz.Width - 20;
            using var timeBrush = new SolidBrush(ArcticColors.HeaderSubText);
            g.DrawString(timeStr, ArcticFonts.HeaderMono, timeBrush, rx, 17);
            rx -= 130;

            // Feed pill
            var pillR = new Rectangle(rx, 11, 120, 26);
            if (pillR.Width > 0 && pillR.Height > 0)
            {
                using var pillPath = ArcticDraw.RoundRect(pillR, 13);
                using var pillFill = new SolidBrush(ArcticColors.HeaderPillBg);
                g.FillPath(pillFill, pillPath);
                using var pillBorder = new Pen(ArcticColors.HeaderPillBorder);
                g.DrawPath(pillBorder, pillPath);
            }

            // Feed dot
            var dotColor = IsLive ? ArcticColors.LiveGreen : ArcticColors.LiveRed;
            ArcticDraw.DrawPulsingDot(g, new Point(rx + 14, 24), 4, dotColor, IsLive ? _pulseAlpha : 1f);

            // Feed text
            string feedText = IsLive ? $"{TicksPerSec} · LIVE" : "DISCONNECTED";
            using var feedBrush = new SolidBrush(Color.FromArgb(220, 235, 250));
            g.DrawString(feedText, ArcticFonts.KpiTag, feedBrush, rx + 24, 16);
        }

        private void DrawIndexPill(Graphics g, ref int x, string name,
            string val, string chg, bool isUp)
        {
            int w = 130, h = 36, pad = 8;
            var r = new Rectangle(x, 6, w, h);

            using var path = ArcticDraw.RoundRect(r, 5);
            using var fill = new SolidBrush(ArcticColors.HeaderPillBg);
            g.FillPath(fill, path);
            using var border = new Pen(ArcticColors.HeaderPillBorder);
            g.DrawPath(border, path);

            using var nameBrush = new SolidBrush(ArcticColors.HeaderIndexName);
            g.DrawString(name, ArcticFonts.KpiLabel, nameBrush, x + pad, r.Top + 3);

            using var valBrush = new SolidBrush(Color.White);
            g.DrawString(val, new Font("Courier New", 9.5f, FontStyle.Bold), valBrush, x + pad, r.Top + 13);

            var chgColor = isUp ? ArcticColors.LiveGreen : Color.FromArgb(255, 138, 128);
            using var chgBrush = new SolidBrush(chgColor);
            g.DrawString((isUp ? "▲" : "▼") + chg, ArcticFonts.KpiLabel, chgBrush, x + pad, r.Top + 25);

            x += w + 8;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _pulseTimer?.Dispose();
            base.Dispose(disposing);
        }
    }
}
