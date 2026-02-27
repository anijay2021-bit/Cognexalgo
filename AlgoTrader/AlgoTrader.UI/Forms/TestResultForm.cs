using AlgoTrader.Brokers.AngelOne;
using AlgoTrader.UI.Theme;

namespace AlgoTrader.UI.Forms;

/// <summary>Displays login test results with green/red step indicators.</summary>
public class TestResultForm : Form
{
    private readonly LoginTestResult _result;

    public TestResultForm(LoginTestResult result)
    {
        _result = result;
        InitializeUI();
    }

    private void InitializeUI()
    {
        Text = $"Login Test — {_result.AccountName}";
        Size = new Size(550, 400);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = ThemeManager.Background;
        ForeColor = ThemeManager.Text;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        // Header
        var header = new Label
        {
            Text = _result.AllPassed
                ? $"✅ ALL TESTS PASSED ({_result.TotalElapsed.TotalSeconds:F1}s)"
                : $"⚠️ {_result.FailCount} STEP(S) FAILED ({_result.TotalElapsed.TotalSeconds:F1}s)",
            Dock = DockStyle.Top,
            Height = 45,
            BackColor = _result.AllPassed ? ThemeManager.PositiveDim : ThemeManager.NegativeDim,
            ForeColor = _result.AllPassed ? ThemeManager.Positive : ThemeManager.Negative,
            Font = AppFonts.SubHeader,
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(10, 0, 10, 0),
        };
        Controls.Add(header);

        // Steps panel
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(15, 10, 15, 10),
            BackColor = ThemeManager.Background,
        };

        foreach (var step in _result.Steps)
        {
            var card = new Panel
            {
                Width = 490,
                Height = 50,
                BackColor = ThemeManager.Card,
                Margin = new Padding(0, 4, 0, 4),
            };

            var icon = new Label
            {
                Text = step.Passed ? "✅" : "❌",
                Location = new Point(10, 8),
                AutoSize = true,
                Font = AppFonts.SubHeader,
            };
            card.Controls.Add(icon);

            var name = new Label
            {
                Text = step.StepName,
                Location = new Point(40, 4),
                AutoSize = true,
                Font = AppFonts.BodyBold,
                ForeColor = ThemeManager.Text,
            };
            card.Controls.Add(name);

            var time = new Label
            {
                Text = $"{step.Elapsed.TotalMilliseconds:F0}ms",
                Location = new Point(420, 4),
                AutoSize = true,
                Font = AppFonts.Tiny,
                ForeColor = ThemeManager.SubText,
            };
            card.Controls.Add(time);

            var detail = new Label
            {
                Text = step.Detail,
                Location = new Point(40, 24),
                AutoSize = true,
                Font = AppFonts.Tiny,
                ForeColor = step.Passed ? ThemeManager.SubText : ThemeManager.Negative,
                MaximumSize = new Size(440, 0),
            };
            card.Controls.Add(detail);

            panel.Controls.Add(card);
        }

        Controls.Add(panel);

        // Close button
        var btnClose = new Button
        {
            Text = "Close",
            Dock = DockStyle.Bottom,
            Height = 35,
            BackColor = ThemeManager.Card,
            ForeColor = ThemeManager.Text,
            FlatStyle = FlatStyle.Flat,
        };
        btnClose.FlatAppearance.BorderColor = ThemeManager.Border;
        btnClose.Click += (s, e) => Close();
        Controls.Add(btnClose);
    }
}
