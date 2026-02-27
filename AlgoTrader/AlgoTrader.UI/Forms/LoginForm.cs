using System.Drawing.Drawing2D;
using AlgoTrader.Core.Enums;
using AlgoTrader.Core.Interfaces;
using AlgoTrader.Core.Models;
using AlgoTrader.Data.Encryption;
using AlgoTrader.Data.Repositories;
using AlgoTrader.UI.Theme;
using Serilog;

namespace AlgoTrader.UI.Forms;

/// <summary>Login form for adding/authenticating broker accounts with TOTP and theming.</summary>
public class LoginForm : Form
{
    /// <summary>
    /// Populated after a successful login. Contains live JWTToken and FeedToken.
    /// Caller must use this instead of reloading from DB (which strips runtime tokens).
    /// </summary>
    public AccountCredential? AuthenticatedCredential { get; private set; }

    private readonly IBrokerFactory _brokerFactory;
    private readonly AccountRepository _accountRepo;
    private readonly CredentialProtector _credentialProtector;
    private readonly ILogger _logger;

    private ComboBox _cmbBroker = null!;
    private TextBox _txtClientId = null!;
    private TextBox _txtPin = null!;
    private TextBox _txtApiKey = null!;
    private TextBox _txtApiSecret = null!;
    private TextBox _txtTotpSecret = null!;
    private TextBox _txtAccountName = null!;
    
    private Label _lblTotpCode = null!;
    private TotpProgressCircle _totpProgress = null!;
    private System.Windows.Forms.Timer _totpTimer = null!;
    private ControlScaler _scaler = null!;

    public LoginForm(IBrokerFactory brokerFactory, AccountRepository accountRepo,
                     CredentialProtector credentialProtector, ILogger logger)
    {
        _brokerFactory = brokerFactory;
        _accountRepo = accountRepo;
        _credentialProtector = credentialProtector;
        _logger = logger;
        InitializeUI();
    }

    private void InitializeUI()
    {
        Text = "Add / Edit Account";
        Size = new Size(500, 680);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;

        // Apply dark theme
        BackColor = ThemeManager.Background;
        ForeColor = ThemeManager.Text;
        Font = AppFonts.Body;

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(25),
            ColumnCount = 2,
            RowCount = 13,
            AutoSize = true
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;
        
        var header = new Label { Text = "🔑 ACCOUNT DETAILS", Font = AppFonts.Header, ForeColor = ThemeManager.Accent, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        panel.Controls.Add(header, 0, row);
        panel.SetColumnSpan(header, 2);
        row++;

        _cmbBroker = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _cmbBroker.Items.AddRange(Enum.GetNames(typeof(BrokerType)));
        _cmbBroker.SelectedIndex = 0;
        AddRow(panel, "Broker:", _cmbBroker, row++);

        _txtAccountName = new TextBox();
        AddRow(panel, "Account Name:", _txtAccountName, row++);

        _txtClientId = new TextBox();
        AddRow(panel, "Client ID:", _txtClientId, row++);

        _txtPin = new TextBox { UseSystemPasswordChar = true };
        AddRow(panel, "PIN / MPIN:", _txtPin, row++);

        _txtApiKey = new TextBox();
        AddRow(panel, "API Key:", _txtApiKey, row++);

        _txtApiSecret = new TextBox { UseSystemPasswordChar = true };
        AddRow(panel, "API Secret:", _txtApiSecret, row++);

        _txtTotpSecret = new TextBox();
        _txtTotpSecret.TextChanged += (s, e) => RefreshTOTP();
        AddRow(panel, "TOTP Secret:", _txtTotpSecret, row++);

        // TOTP Display Row
        var totpPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoSize = true, Padding = new Padding(0, 5, 0, 5) };
        _lblTotpCode = new Label 
        { 
            Text = "------", 
            Font = AppFonts.PriceLg, 
            ForeColor = ThemeManager.Positive,
            AutoSize = true,
            Padding = new Padding(0, 5, 10, 0)
        };
        _totpProgress = new TotpProgressCircle { Width = 30, Height = 30 };
        totpPanel.Controls.Add(_lblTotpCode);
        totpPanel.Controls.Add(_totpProgress);
        
        var lblTotpTitle = new Label { Text = "Current TOTP:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = ThemeManager.SubText };
        panel.Controls.Add(lblTotpTitle, 0, row);
        panel.Controls.Add(totpPanel, 1, row++);

        // Buttons
        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 20, 0, 0) };
        
        var btnLogin = new Button { Text = "Login & Save", Width = 140, Height = 38, BackColor = ThemeManager.PositiveDim, ForeColor = ThemeManager.Positive, FlatStyle = FlatStyle.Flat, Font = AppFonts.BodyBold };
        btnLogin.FlatAppearance.BorderColor = ThemeManager.Positive;
        btnLogin.Click += async (s, e) => await LoginAndSaveAsync();

        var btnSave = new Button { Text = "Save Offline", Width = 120, Height = 38, BackColor = ThemeManager.Card, ForeColor = ThemeManager.Text, FlatStyle = FlatStyle.Flat, Font = AppFonts.Body };
        btnSave.FlatAppearance.BorderColor = ThemeManager.Border;
        btnSave.Click += (s, e) => SaveAccount();

        btnPanel.Controls.Add(btnLogin);
        btnPanel.Controls.Add(btnSave);
        panel.Controls.Add(btnPanel, 0, row);
        panel.SetColumnSpan(btnPanel, 2);

        Controls.Add(panel);
        ThemeManager.ApplyTheme(this);

        _totpTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _totpTimer.Tick += (s, e) => RefreshTOTP();
        _totpTimer.Start();
    }

    private void AddRow(TableLayoutPanel panel, string label, Control input, int row)
    {
        panel.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = ThemeManager.SubText }, 0, row);
        input.Dock = DockStyle.Fill;
        panel.Controls.Add(input, 1, row);
    }

    private void RefreshTOTP()
    {
        // Update remaining seconds on progress circle
        var now = DateTime.UtcNow;
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        long elapsedSeconds = (long)(now - epoch).TotalSeconds;
        int remaining = 30 - (int)(elapsedSeconds % 30);
        _totpProgress.RemainingSeconds = remaining;

        if (string.IsNullOrEmpty(_txtTotpSecret.Text)) 
        {
            _lblTotpCode.Text = "------";
            return;
        }

        try
        {
            var key = OtpNet.Base32Encoding.ToBytes(_txtTotpSecret.Text.Trim().Replace(" ", ""));
            var totp = new OtpNet.Totp(key);
            _lblTotpCode.Text = totp.ComputeTotp();
        }
        catch { _lblTotpCode.Text = "ERROR"; }
    }

    private AccountCredential BuildCredential() => new()
    {
        ClientID = _txtClientId.Text.Trim(),
        Password = _txtPin.Text, // Same as MPIN for Angel One
        PIN = _txtPin.Text,
        APIKey = _txtApiKey.Text.Trim(),
        APISecret = _txtApiSecret.Text,
        TOTPSecret = _txtTotpSecret.Text.Trim().Replace(" ", ""),
        BrokerType = Enum.Parse<BrokerType>(_cmbBroker.SelectedItem!.ToString()!),
        AccountName = _txtAccountName.Text.Trim(),
        GroupName = "Individual",
    };

    private async Task LoginAndSaveAsync()
    {
        var credential = BuildCredential();
        var broker = _brokerFactory.Create(credential.BrokerType);

        Cursor = Cursors.WaitCursor;
        try
        {
            var success = await broker.LoginAsync(credential);
            if (success)
            {
                AuthenticatedCredential = credential;   // preserve live tokens for caller
                SaveCredentialToDb(credential);
                MessageBox.Show("Login successful! Token saved.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                MessageBox.Show("Login failed. Check credentials.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Login error");
            MessageBox.Show($"Login error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { Cursor = Cursors.Default; }
    }

    private void SaveAccount()
    {
        var credential = BuildCredential();
        SaveCredentialToDb(credential);
        MessageBox.Show("Account saved offline.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void SaveCredentialToDb(AccountCredential cred)
    {
        var encrypted = _credentialProtector.ProtectCredential(cred);
        _accountRepo.Upsert(encrypted);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _scaler = new ControlScaler(this);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _scaler?.Scale();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _totpTimer?.Stop();
        base.OnFormClosing(e);
    }
}

/// <summary>Circular progress bar showing remaining seconds for TOTP.</summary>
public class TotpProgressCircle : Control
{
    private int _remainingSeconds = 30;

    public int RemainingSeconds
    {
        get => _remainingSeconds;
        set { _remainingSeconds = Math.Clamp(value, 0, 30); Invalidate(); }
    }

    public TotpProgressCircle()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var rect = new Rectangle(2, 2, Width - 5, Height - 5);
        using var bgPen = new Pen(ThemeManager.Border, 3);
        g.DrawEllipse(bgPen, rect);

        float sweepAngle = -(_remainingSeconds / 30f) * 360f;
        Color progressColor = _remainingSeconds <= 5 ? ThemeManager.Negative : ThemeManager.Accent;
        using var progressPen = new Pen(progressColor, 3);
        g.DrawArc(progressPen, rect, 270, sweepAngle);

        using var numBrush = new SolidBrush(ThemeManager.Text);
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(_remainingSeconds.ToString(), new Font("Segoe UI", 8), numBrush, rect, fmt);
    }
}
