using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using AlgoTrader.UI.Theme;

namespace AlgoTrader.UI.Controls
{
    public enum AlertBadgeType { Entry, Exit, SL }

    public class ArcticAlertsPanel : UserControl
    {
        public string Title { get; set; } = "Recent Signals & Alerts";

        private class AlertItem
        {
            public string Icon;
            public string Text;
            public string Badge;
            public string Time;
            public AlertBadgeType Type;
        }

        private List<AlertItem> _alerts = new List<AlertItem>();

        public ArcticAlertsPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint, true);
        }

        public void AddAlert(string icon, string text, string badge, string time, AlertBadgeType type)
        {
            _alerts.Insert(0, new AlertItem { Icon = icon, Text = text, Badge = badge, Time = time, Type = type });
            if (_alerts.Count > 50) _alerts.RemoveAt(_alerts.Count - 1);
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

            int y = 50;
            int rowH = 36;
            for (int i = 0; i < _alerts.Count; i++)
            {
                if (y + rowH > Height - 10) break;

                var alert = _alerts[i];
                
                // Icon
                g.DrawString(alert.Icon, ArcticFonts.AlertText, Brushes.Black, 18, y + 8);

                // Text
                using var textBrush = new SolidBrush(ArcticColors.TextNav);
                g.DrawString(alert.Text, ArcticFonts.AlertText, textBrush, 40, y + 8);

                // Time
                using var timeBrush = new SolidBrush(ArcticColors.TextMuted);
                var timeSz = g.MeasureString(alert.Time, ArcticFonts.AlertTime);
                g.DrawString(alert.Time, ArcticFonts.AlertTime, timeBrush, Width - timeSz.Width - 18, y + 10);

                // Badge
                Color bg = ArcticColors.BadgeEntryBg;
                Color fg = ArcticColors.BadgeEntryText;
                if (alert.Type == AlertBadgeType.Exit) { bg = ArcticColors.BadgeExitBg; fg = ArcticColors.BadgeExitText; }
                if (alert.Type == AlertBadgeType.SL)   { bg = ArcticColors.BadgeSlBg;   fg = ArcticColors.BadgeSlText;   }

                var badgeSz = g.MeasureString(alert.Badge, ArcticFonts.KpiTag);
                var badgeR = new Rectangle(Width - (int)timeSz.Width - (int)badgeSz.Width - 45, y + 8, (int)badgeSz.Width + 14, 18);
                ArcticDraw.DrawBadge(g, badgeR, alert.Badge, bg, fg, 9);

                // Separator
                using var sepPen = new Pen(ArcticColors.BorderLight);
                g.DrawLine(sepPen, 18, y + rowH, Width - 18, y + rowH);

                y += rowH;
            }
        }
    }
}
