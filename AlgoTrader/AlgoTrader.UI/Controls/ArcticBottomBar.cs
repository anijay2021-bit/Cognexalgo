using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using AlgoTrader.UI.Theme;

namespace AlgoTrader.UI.Controls
{
    public class ArcticBottomBar : Panel
    {
        public string FeedStatus  { get; set; } = "Live";
        public int    TicksPerSec { get; set; } = 0;
        public string LastTick    { get; set; } = "--:--:--";
        public string TokenTTL    { get; set; } = "--";
        public decimal TotalMTM   { get; set; } = 0m;
        public bool   IsLive      { get; set; } = true;

        public ArcticBottomBar()
        {
            Height = 26;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            ArcticDraw.DrawBottomBar(g, ClientRectangle);

            int x = 14;
            using var labelBrush = new SolidBrush(ArcticColors.TextLabel);
            using var valBrush   = new SolidBrush(ArcticColors.TextNav);
            using var sepBrush   = new SolidBrush(ArcticColors.TextMuted);

            // Feed dot
            var dotColor = IsLive ? ArcticColors.ProfitGreenLt : ArcticColors.LossRed;
            using var dotBrush = new SolidBrush(dotColor);
            g.FillEllipse(dotBrush, x, 10, 6, 6);
            x += 10;

            void DrawItem(string label, string val)
            {
                g.DrawString(label, ArcticFonts.BottomBar, labelBrush, x, 6);
                x += (int)g.MeasureString(label, ArcticFonts.BottomBar).Width;
                g.DrawString(val, ArcticFonts.BottomMono, valBrush, x, 5);
                x += (int)g.MeasureString(val, ArcticFonts.BottomMono).Width + 2;
            }
            void DrawSep()
            {
                g.DrawString(" | ", ArcticFonts.BottomBar, sepBrush, x, 6);
                x += (int)g.MeasureString(" | ", ArcticFonts.BottomBar).Width;
            }

            DrawItem("Feed: ", FeedStatus);
            DrawSep();
            DrawItem("Ticks/sec: ", TicksPerSec.ToString());
            DrawSep();
            DrawItem("Last Tick: ", LastTick);
            DrawSep();
            DrawItem("Token TTL: ", TokenTTL);

            // MTM (right-aligned)
            bool isPos = TotalMTM >= 0;
            string mtmStr = $"MTM: {(isPos ? "+" : "")}₹{TotalMTM:N2}";
            var mtmColor = isPos ? ArcticColors.ProfitGreen : ArcticColors.LossRed;
            using var mtmBrush = new SolidBrush(mtmColor);
            var mtmSz = g.MeasureString(mtmStr, ArcticFonts.BottomMTM);
            g.DrawString(mtmStr, ArcticFonts.BottomMTM, mtmBrush,
                Width - (int)mtmSz.Width - 14, 5);
        }
    }
}
