using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using AlgoTrader.UI.Theme;

namespace AlgoTrader.UI.Controls
{
    public class ArcticMenuButton : Control
    {
        public string Icon      { get; set; } = "●";
        public Color  DotColor  { get; set; } = ArcticColors.AccentBlue;
        public bool   IsActive  { get; set; } = false;
        private bool  _hover    = false;

        public ArcticMenuButton()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint, true);
            Cursor = Cursors.Hand;
            Height = 24;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (r.Width > 0 && r.Height > 0)
            {
                using var path = ArcticDraw.RoundRect(r, 4);

                // Background
                Color bg = IsActive ? ArcticColors.ActiveBg
                         : _hover   ? ArcticColors.HoverBg
                         : Color.Transparent;
                if (bg != Color.Transparent)
                {
                    using var fill = new SolidBrush(bg);
                    g.FillPath(fill, path);
                }

                // Border
                if (IsActive || _hover)
                {
                    using var border = new Pen(IsActive ? ArcticColors.ActiveBorder : ArcticColors.BorderLight);
                    g.DrawPath(border, path);
                }
            }

            int x = 8;
            // Dot
            using var dotBrush = new SolidBrush(DotColor);
            g.FillEllipse(dotBrush, x, 8, 7, 7);
            x += 12;

            // Text
            var fg = IsActive ? ArcticColors.TextNav : ArcticColors.TextAccent;
            using var textBrush = new SolidBrush(fg);
            g.DrawString(Text, ArcticFonts.MenuBtn, textBrush, x, 4);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true;  Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    }
}
