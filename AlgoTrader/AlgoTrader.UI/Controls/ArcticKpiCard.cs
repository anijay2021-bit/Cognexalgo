using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using AlgoTrader.UI.Theme;

namespace AlgoTrader.UI.Controls
{
    public class ArcticKpiCard : UserControl
    {
        public string Label     { get; set; } = "TOTAL MTM";
        public string Value     { get; set; } = "₹0.00";
        public string SubText   { get; set; } = "";
        public string Tag       { get; set; } = "";
        public bool   IsProfit  { get; set; } = false;
        public bool   IsBlue    { get; set; } = false;

        public ArcticKpiCard()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint, true);
            Height = 100;
            Padding = new Padding(18, 16, 18, 12);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Shadow
            var shadowR = new Rectangle(2, 4, Width - 4, Height - 4);
            if (shadowR.Width > 0 && shadowR.Height > 0)
            {
                using var shadowBrush = new SolidBrush(Color.FromArgb(18, 20, 80, 160));
                using var shadowPath = ArcticDraw.RoundRect(shadowR, 10);
                g.FillPath(shadowBrush, shadowPath);
            }

            // Card body
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (r.Width > 0 && r.Height > 0)
            {
                using var path = ArcticDraw.RoundRect(r, 10);
                using var fill = new SolidBrush(ArcticColors.CardBg);
                g.FillPath(fill, path);
                using var border = new Pen(ArcticColors.CardBorder);
                g.DrawPath(border, path);
            }

            // Top bar gradient (3px)
            var barR = new Rectangle(10, 0, Width - 20, 3);
            if (barR.Width > 0 && barR.Height > 0)
            {
                using var barBrush = new LinearGradientBrush(barR,
                    ArcticColors.AccentBlue, ArcticColors.AccentBlueLt, 0f);
                g.FillRectangle(barBrush, barR);
            }

            int y = 12;

            // Label
            using var lblBrush = new SolidBrush(ArcticColors.TextLabel);
            g.DrawString(Label.ToUpper(), ArcticFonts.KpiLabel, lblBrush, 18, y);
            y += 18;

            // Value
            var valColor = IsProfit ? ArcticColors.ProfitGreen
                         : IsBlue  ? ArcticColors.AccentBlue
                                    : ArcticColors.TextPrimary;
            using var valBrush = new SolidBrush(valColor);
            g.DrawString(Value, ArcticFonts.KpiValue, valBrush, 18, y);
            y += 28;

            // Sub text
            if (!string.IsNullOrEmpty(SubText))
            {
                using var subBrush = new SolidBrush(ArcticColors.TextMuted);
                g.DrawString(SubText, ArcticFonts.KpiSub, subBrush, 18, y);
                y += 14;
            }

            // Tag pill
            if (!string.IsNullOrEmpty(Tag))
            {
                var tagSz = g.MeasureString(Tag, ArcticFonts.KpiTag);
                var tagR = new Rectangle(18, y, (int)tagSz.Width + 16, 16);
                if (tagR.Width > 0 && tagR.Height > 0)
                {
                    using var tagPath = ArcticDraw.RoundRect(tagR, 8);
                    using var tagFill = new SolidBrush(ArcticColors.HoverBg);
                    g.FillPath(tagFill, tagPath);
                    using var tagBrush = new SolidBrush(ArcticColors.AccentBlue);
                    g.DrawString(Tag, ArcticFonts.KpiTag, tagBrush, tagR.X + 8, tagR.Y + 2);
                }
            }
        }

        public void UpdateValue(string val, bool isProfit)
        {
            Value = val;
            IsProfit = isProfit;
            this.Invalidate();
        }
    }
}
