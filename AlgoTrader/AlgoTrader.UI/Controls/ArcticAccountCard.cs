using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using AlgoTrader.UI.Theme;

namespace AlgoTrader.UI.Controls
{
    public class ArcticAccountCard : UserControl
    {
        public string AccountId  { get; set; } = "A1234";
        public string BrokerName { get; set; } = "Angel One";
        public decimal MTM       { get; set; } = 0m;
        public bool IsConnected  { get; set; } = true;
        private bool _selected   = false;

        public bool Selected
        {
            get => _selected;
            set { _selected = value; Invalidate(); }
        }

        public ArcticAccountCard()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint, true);
            Height = 68;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (r.Width > 0 && r.Height > 0)
            {
                using var path = ArcticDraw.RoundRect(r, 7);

                // Fill
                using var fill = new SolidBrush(_selected
                    ? Color.FromArgb(240, 248, 255)
                    : ArcticColors.CardBg);
                g.FillPath(fill, path);

                // Border
                var borderColor = _selected ? ArcticColors.BorderMedium : ArcticColors.CardBorder;
                using var border = new Pen(borderColor);
                g.DrawPath(border, path);

                // Left accent bar (3px, only when selected)
                if (_selected)
                {
                    using var accentBrush = new SolidBrush(ArcticColors.SelectedCardLeft);
                    g.FillRectangle(accentBrush, 0, 7, 3, Height - 14);
                }
            }

            int lx = _selected ? 12 : 10;

            // Status dot
            var dotColor = IsConnected ? ArcticColors.ProfitGreenLt : ArcticColors.LossRed;
            using var dotBrush = new SolidBrush(dotColor);
            g.FillEllipse(dotBrush, lx, 12, 7, 7);

            // Account ID
            using var nameBrush = new SolidBrush(ArcticColors.TextNav);
            g.DrawString(AccountId, ArcticFonts.AccName, nameBrush, lx + 12, 9);

            // Broker name
            using var brokerBrush = new SolidBrush(ArcticColors.TextMuted);
            g.DrawString(BrokerName, ArcticFonts.AccBroker, brokerBrush, lx, 26);

            // MTM
            bool isPos = MTM > 0;
            var mtmColor = isPos ? ArcticColors.ProfitGreen : (MTM < 0 ? ArcticColors.LossRed : ArcticColors.TextMuted);
            string mtmStr = MTM == 0 ? "₹0.00" : (isPos ? "+" : "") + $"₹{MTM:N2}";
            using var mtmBrush = new SolidBrush(mtmColor);
            g.DrawString(mtmStr, ArcticFonts.AccMTM, mtmBrush, lx, 42);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            if (!_selected) BackColor = ArcticColors.HoverBg;
            base.OnMouseEnter(e);
        }
        protected override void OnMouseLeave(EventArgs e)
        {
            if (!_selected) BackColor = ArcticColors.CardBg;
            base.OnMouseLeave(e);
        }
    }
}
