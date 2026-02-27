using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using AlgoTrader.UI.Theme;

namespace AlgoTrader.UI.Controls
{
    public class ArcticChartPanel : UserControl
    {
        public string Title { get; set; } = "Portfolio P&L Curve";
        public string SubTitle { get; set; } = "9:15 AM → 3:30 PM · Today";
        private List<float> _data = new List<float>();

        public ArcticChartPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint, true);
        }

        public void LoadSampleData()
        {
            _data = new List<float> { 0, 500, 300, 800, 1200, 1100, 1500, 2400, 2100, 2800, 3500, 3200, 4000 };
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Draw Card Background
            var cardR = new Rectangle(0, 0, Width - 1, Height - 1);
            ArcticDraw.DrawCard(g, cardR);

            // Title
            using var titleBrush = new SolidBrush(ArcticColors.TextHeader);
            g.DrawString(Title, ArcticFonts.ChartTitle, titleBrush, 18, 14);

            using var subBrush = new SolidBrush(ArcticColors.TextMuted);
            g.DrawString(SubTitle, ArcticFonts.ChartSub, subBrush, 18, 30);

            // Chart area
            var chartR = new Rectangle(18, 55, Width - 36, Height - 75);
            if (chartR.Width > 0 && chartR.Height > 0 && _data.Count > 0)
            {
                float min = 0;
                float max = 0;
                foreach (var v in _data) { if (v < min) min = v; if (v > max) max = v; }
                if (max == min) max = min + 1;

                ArcticDraw.DrawEquityCurve(g, chartR, _data, min, max);
            }
        }
    }
}
