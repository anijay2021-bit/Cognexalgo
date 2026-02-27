using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using AlgoTrader.UI.Theme;

namespace AlgoTrader.UI.Controls
{
    public class ArcticTabStrip : UserControl
    {
        public class TabItem
        {
            public string Icon  { get; set; } = "";
            public string Label { get; set; } = "";
            public Color  DotColor { get; set; } = ArcticColors.AccentBlue;
        }

        private List<TabItem> _tabs = new();
        private int _selectedIndex = 0;
        private int _hoverIndex = -1;

        public event EventHandler<int> TabChanged;
        public int SelectedIndex { get => _selectedIndex; set { _selectedIndex = value; Invalidate(); } }

        public void AddTab(string icon, string label, Color? dotColor = null)
        {
            _tabs.Add(new TabItem {
                Icon = icon, Label = label,
                DotColor = dotColor ?? ArcticColors.AccentBlue
            });
            Invalidate();
        }

        public ArcticTabStrip()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint, true);
            Height = 40;
            BackColor = ArcticColors.TabBarBg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Background
            g.Clear(ArcticColors.TabBarBg);

            // Bottom border of tab bar
            using var barPen = new Pen(ArcticColors.TabBorder, 2);
            g.DrawLine(barPen, 0, Height - 2, Width, Height - 2);

            int x = 16;
            for (int i = 0; i < _tabs.Count; i++)
            {
                var tab = _tabs[i];
                bool isActive = (i == _selectedIndex);
                bool isHover  = (i == _hoverIndex && !isActive);

                string text = $"{tab.Icon} {tab.Label}";
                var font = isActive ? ArcticFonts.TabActive : ArcticFonts.TabNormal;
                var sz = g.MeasureString(text, font);
                int w = (int)sz.Width + 24;
                var r = new Rectangle(x, 4, w, Height - 4);

                if (isActive)
                {
                    // Active tab: white bg with borders except bottom
                    using var path = ArcticDraw.RoundRect(
                        new Rectangle(r.X, r.Y, r.Width, r.Height + 2), 6);
                    using var fill = new SolidBrush(ArcticColors.ActiveTabBg);
                    g.FillPath(fill, path);
                    using var borderPen = new Pen(ArcticColors.TabBorder);
                    g.DrawPath(borderPen, path);
                    // Mask bottom border
                    using var maskBrush = new SolidBrush(ArcticColors.ActiveTabBg);
                    g.FillRectangle(maskBrush, r.X + 1, r.Bottom - 2, r.Width - 2, 4);

                    using var textBrush = new SolidBrush(ArcticColors.TextAccent);
                    g.DrawString(text, font, textBrush,
                        r.X + 12, r.Y + (r.Height - sz.Height) / 2 + 2);
                }
                else if (isHover)
                {
                    using var path = ArcticDraw.RoundRect(r, 6);
                    using var fill = new SolidBrush(ArcticColors.HoverBg);
                    g.FillPath(fill, path);

                    using var textBrush = new SolidBrush(ArcticColors.AccentBlue);
                    g.DrawString(text, font, textBrush,
                        r.X + 12, r.Y + (r.Height - sz.Height) / 2 + 2);
                }
                else
                {
                    using var textBrush = new SolidBrush(Color.FromArgb(130, 128, 168));
                    g.DrawString(text, font, textBrush,
                        r.X + 12, r.Y + (r.Height - sz.Height) / 2 + 2);
                }
                x += w + 2;
            }
        }

        private int HitTestTab(Point pt)
        {
            using var g = CreateGraphics();
            int x = 16;
            for (int i = 0; i < _tabs.Count; i++)
            {
                var tab = _tabs[i];
                var font = i == _selectedIndex ? ArcticFonts.TabActive : ArcticFonts.TabNormal;
                string text = $"{tab.Icon} {tab.Label}";
                int w = (int)g.MeasureString(text, font).Width + 24;
                if (new Rectangle(x, 0, w, Height).Contains(pt)) return i;
                x += w + 2;
            }
            return -1;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int idx = HitTestTab(e.Location);
            if (idx >= 0 && idx != _selectedIndex)
            {
                _selectedIndex = idx;
                Invalidate();
                TabChanged?.Invoke(this, idx);
            }
            base.OnMouseClick(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTestTab(e.Location);
            if (h != _hoverIndex) { _hoverIndex = h; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverIndex = -1; Invalidate();
            base.OnMouseLeave(e);
        }
    }
}
