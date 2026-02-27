using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AlgoTrader.UI.Forms
{
    public class ControlScaler
    {
        private float _originalWidth;
        private float _originalHeight;
        private Control _container;
        private Dictionary<Control, Rectangle> _originalBounds;
        private Dictionary<Control, float> _originalFontSizes;

        public ControlScaler(Control container)
        {
            _container = container;
            _originalWidth = container.Width;
            _originalHeight = container.Height;
            _originalBounds = new Dictionary<Control, Rectangle>();
            _originalFontSizes = new Dictionary<Control, float>();

            CaptureOriginalBounds(container);
        }

        private void CaptureOriginalBounds(Control parent)
        {
            foreach (Control ctrl in parent.Controls)
            {
                // Skip controls that are docked or have fill properties, 
                // but since the user asked for a recursive scaling logic based on ratios, we store all.
                _originalBounds[ctrl] = new Rectangle(ctrl.Location, ctrl.Size);
                if (ctrl.Font != null)
                {
                    _originalFontSizes[ctrl] = ctrl.Font.Size;
                }

                if (ctrl.Controls.Count > 0)
                {
                    CaptureOriginalBounds(ctrl);
                }
            }
        }

        public void Scale()
        {
            if (_originalWidth <= 0 || _originalHeight <= 0 || _container.Width <= 0 || _container.Height <= 0) return;

            float ratioX = _container.Width / _originalWidth;
            float ratioY = _container.Height / _originalHeight;

            _container.SuspendLayout();
            ScaleControls(_container, ratioX, ratioY);
            _container.ResumeLayout();
        }

        private void ScaleControls(Control parent, float ratioX, float ratioY)
        {
            foreach (Control ctrl in parent.Controls)
            {
                // If it's docked, we shouldn't manually resize its bounding box, to avoid fighting the layout engine.
                // But we will follow the prompt's request for recursive scaling.
                if (ctrl.Dock == DockStyle.None)
                {
                    if (_originalBounds.TryGetValue(ctrl, out Rectangle bounds))
                    {
                        ctrl.Left = (int)(bounds.Left * ratioX);
                        ctrl.Top = (int)(bounds.Top * ratioY);
                        ctrl.Width = (int)(bounds.Width * ratioX);
                        ctrl.Height = (int)(bounds.Height * ratioY);
                    }
                }

                if (_originalFontSizes.TryGetValue(ctrl, out float fontSize))
                {
                    float newFontSize = fontSize * Math.Min(ratioX, ratioY);
                    if (newFontSize > 4) // prevent extremely small fonts
                    {
                        ctrl.Font = new Font(ctrl.Font.FontFamily, newFontSize, ctrl.Font.Style);
                    }
                }

                if (ctrl.Controls.Count > 0)
                {
                    ScaleControls(ctrl, ratioX, ratioY);
                }
            }
        }
    }
}
