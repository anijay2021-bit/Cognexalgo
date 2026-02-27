using System.Reactive.Linq;
using AlgoTrader.Brokers.AngelOne;
using AlgoTrader.Core.Enums;
using AlgoTrader.Core.Interfaces;
using AlgoTrader.Core.Models;
using Serilog;

namespace AlgoTrader.UI.Forms;

/// <summary>Option chain viewer form — displays CE/PE grid for a given underlying + expiry.</summary>
public class OptionChainForm : Form
{
    private readonly InstrumentMasterService _instrumentService;
    private readonly IMarketDataService _marketData;
    private readonly ILogger _logger;

    private ComboBox _cmbUnderlying = null!;
    private ComboBox _cmbExpiry = null!;
    private DataGridView _chainGrid = null!;
    private Label _lblSpot = null!;
    private Label _lblAtm = null!;

    private IDisposable? _tickSubscription;
    private readonly Dictionary<string, DataGridViewCell> _ceLtpCells = new();
    private readonly Dictionary<string, DataGridViewCell> _ceOiCells = new();
    private readonly Dictionary<string, DataGridViewCell> _peLtpCells = new();
    private readonly Dictionary<string, DataGridViewCell> _peOiCells = new();
    private readonly List<(Exchange, string)> _currentSubscriptions = new();
    private ControlScaler _scaler = null!;

    public OptionChainForm(InstrumentMasterService instrumentService, IMarketDataService marketData, ILogger logger)
    {
        _instrumentService = instrumentService;
        _marketData = marketData;
        _logger = logger;
        InitializeUI();
        SubscribeToTicks();
    }

    private void InitializeUI()
    {
        Text = "Cognex Algo — Option Chain";
        try { Icon = new Icon(@"Assets\icon.ico"); } catch { }
        Size = new Size(1100, 650);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(20, 20, 30);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9.5f);

        // ─── Top bar ───
        var topPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 50,
            BackColor = Color.FromArgb(28, 28, 42),
            Padding = new Padding(10, 10, 10, 5),
            WrapContents = false
        };

        topPanel.Controls.Add(new Label { Text = "Underlying:", AutoSize = true, ForeColor = Color.White, Margin = new Padding(0, 5, 5, 0) });
        _cmbUnderlying = new ComboBox
        {
            Width = 140,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(35, 35, 55),
            ForeColor = Color.White,
        };
        _cmbUnderlying.Items.AddRange(new[] { "NIFTY", "BANKNIFTY", "FINNIFTY", "MIDCPNIFTY", "SENSEX" });
        _cmbUnderlying.SelectedIndex = 0;
        _cmbUnderlying.SelectedIndexChanged += (s, e) => LoadExpiries();
        topPanel.Controls.Add(_cmbUnderlying);

        topPanel.Controls.Add(new Label { Text = "  Expiry:", AutoSize = true, ForeColor = Color.White, Margin = new Padding(15, 5, 5, 0) });
        _cmbExpiry = new ComboBox
        {
            Width = 140,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(35, 35, 55),
            ForeColor = Color.White,
        };
        _cmbExpiry.SelectedIndexChanged += (s, e) => LoadChain();
        topPanel.Controls.Add(_cmbExpiry);

        _lblSpot = new Label { Text = "Spot: --", AutoSize = true, ForeColor = Color.LimeGreen, Margin = new Padding(30, 5, 5, 0), Font = new Font("Segoe UI Semibold", 11f) };
        topPanel.Controls.Add(_lblSpot);

        _lblAtm = new Label { Text = "ATM: --", AutoSize = true, ForeColor = Color.Cyan, Margin = new Padding(20, 5, 5, 0), Font = new Font("Segoe UI Semibold", 11f) };
        topPanel.Controls.Add(_lblAtm);

        Controls.Add(topPanel);

        // ─── Grid ───
        _chainGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Color.FromArgb(22, 22, 35),
            ForeColor = Color.White,
            GridColor = Color.FromArgb(40, 40, 60),
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(30, 30, 50),
                ForeColor = Color.FromArgb(180, 200, 255),
                Font = new Font("Segoe UI Semibold", 9f),
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                SelectionBackColor = Color.FromArgb(40, 40, 65)
            },
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(22, 22, 35),
                ForeColor = Color.White,
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                SelectionBackColor = Color.FromArgb(50, 50, 80),
            },
            RowHeadersVisible = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            EnableHeadersVisualStyles = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };

        _chainGrid.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name = "CE_OI", HeaderText = "CE OI", Width = 80 },
            new DataGridViewTextBoxColumn { Name = "CE_LTP", HeaderText = "CE LTP", Width = 80 },
            new DataGridViewTextBoxColumn { Name = "CE_Symbol", HeaderText = "CE Symbol", Width = 200 },
            new DataGridViewTextBoxColumn { Name = "Strike", HeaderText = "STRIKE", Width = 90 },
            new DataGridViewTextBoxColumn { Name = "PE_Symbol", HeaderText = "PE Symbol", Width = 200 },
            new DataGridViewTextBoxColumn { Name = "PE_LTP", HeaderText = "PE LTP", Width = 80 },
            new DataGridViewTextBoxColumn { Name = "PE_OI", HeaderText = "PE OI", Width = 80 },
        });

        Controls.Add(_chainGrid);

        LoadExpiries();
    }

    private void LoadExpiries()
    {
        _cmbExpiry.Items.Clear();
        var underlying = _cmbUnderlying.SelectedItem?.ToString() ?? "NIFTY";
        var exchange = underlying is "SENSEX" or "BANKEX" ? Exchange.BFO : Exchange.NFO;

        var expiries = _instrumentService.GetExpiries(underlying, exchange);
        foreach (var exp in expiries)
            _cmbExpiry.Items.Add(exp.ToString("dd-MMM-yyyy"));

        if (_cmbExpiry.Items.Count > 0)
            _cmbExpiry.SelectedIndex = 0;
    }

    private async void LoadChain()
    {
        _chainGrid.Rows.Clear();
        _ceLtpCells.Clear();
        _ceOiCells.Clear();
        _peLtpCells.Clear();
        _peOiCells.Clear();

        if (_currentSubscriptions.Count > 0)
        {
            await _marketData.UnsubscribeAsync(_currentSubscriptions);
            _currentSubscriptions.Clear();
        }

        var underlying = _cmbUnderlying.SelectedItem?.ToString() ?? "NIFTY";
        var exchange = underlying is "SENSEX" or "BANKEX" ? Exchange.BFO : Exchange.NFO;

        if (_cmbExpiry.SelectedItem == null) return;
        if (!DateTime.TryParse(_cmbExpiry.SelectedItem.ToString(), out var expiry)) return;

        var chain = _instrumentService.BuildOptionChain(underlying, expiry, exchange);
        if (chain.Entries.Count == 0) return;

        var tokensToSubscribe = new List<(Exchange, string)>();

        foreach (var entry in chain.Entries)
        {
            int rowIndex = _chainGrid.Rows.Add(
                "", // CE OI
                "", // CE LTP
                entry.CE?.Symbol ?? "",
                entry.Strike.ToString("F0"),
                entry.PE?.Symbol ?? "",
                "", // PE LTP
                ""  // PE OI
            );

            var row = _chainGrid.Rows[rowIndex];

            if (entry.CE != null)
            {
                _ceOiCells[entry.CE.Token] = row.Cells["CE_OI"];
                _ceLtpCells[entry.CE.Token] = row.Cells["CE_LTP"];
                tokensToSubscribe.Add((exchange, entry.CE.Token));
            }

            if (entry.PE != null)
            {
                _peOiCells[entry.PE.Token] = row.Cells["PE_OI"];
                _peLtpCells[entry.PE.Token] = row.Cells["PE_LTP"];
                tokensToSubscribe.Add((exchange, entry.PE.Token));
            }
        }

        if (tokensToSubscribe.Any())
        {
            _currentSubscriptions.AddRange(tokensToSubscribe);
            await _marketData.SubscribeAsync(tokensToSubscribe, SubscriptionMode.LTP);
        }

        _logger.Information("Loaded option chain for {Underlying} {Expiry}: {Strikes} strikes",
            underlying, expiry.ToString("dd-MMM-yyyy"), chain.Entries.Count);
    }

    private void SubscribeToTicks()
    {
        _tickSubscription = _marketData.TickStream
            .ObserveOn(SynchronizationContext.Current!)
            .Subscribe(tick =>
            {
                if (_ceLtpCells.TryGetValue(tick.Token, out var ceLtpCell))
                {
                    ceLtpCell.Value = tick.LTP.ToString("F2");
                    ceLtpCell.Style.ForeColor = Color.SpringGreen;
                    if (_ceOiCells.TryGetValue(tick.Token, out var ceOiCell))
                        ceOiCell.Value = tick.OI > 0 ? (tick.OI / 1000.0).ToString("F1") + "k" : "";
                }
                else if (_peLtpCells.TryGetValue(tick.Token, out var peLtpCell))
                {
                    peLtpCell.Value = tick.LTP.ToString("F2");
                    peLtpCell.Style.ForeColor = Color.Tomato;
                    if (_peOiCells.TryGetValue(tick.Token, out var peOiCell))
                        peOiCell.Value = tick.OI > 0 ? (tick.OI / 1000.0).ToString("F1") + "k" : "";
                }
            });
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
        _tickSubscription?.Dispose();
        if (_currentSubscriptions.Count > 0)
        {
            _marketData.UnsubscribeAsync(_currentSubscriptions).ConfigureAwait(false);
        }
        base.OnFormClosing(e);
    }
}
