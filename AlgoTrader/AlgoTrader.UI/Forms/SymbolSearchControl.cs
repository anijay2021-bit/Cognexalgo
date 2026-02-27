using AlgoTrader.Brokers.AngelOne;
using AlgoTrader.Core.Models;
using Serilog;

namespace AlgoTrader.UI.Forms;

/// <summary>Reusable symbol search textbox with autocomplete dropdown.</summary>
public class SymbolSearchControl : UserControl
{
    private readonly TextBox _txtSearch;
    private readonly ListBox _lstResults;
    private readonly InstrumentMasterService _instrumentService;
    private readonly ILogger _logger;
    private System.Windows.Forms.Timer _debounceTimer;
    private InstrumentMaster? _selectedInstrument;
    private ControlScaler _scaler = null!;

    public event EventHandler<InstrumentMaster>? InstrumentSelected;
    public InstrumentMaster? SelectedInstrument => _selectedInstrument;

    public SymbolSearchControl(InstrumentMasterService instrumentService, ILogger logger)
    {
        _instrumentService = instrumentService;
        _logger = logger;

        // TextBox
        _txtSearch = new TextBox
        {
            Dock = DockStyle.Top,
            Height = 28,
            Font = new Font("Segoe UI", 10f),
            BackColor = Color.FromArgb(35, 35, 55),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "🔍  Search symbol (e.g. NIFTY, RELIANCE)..."
        };
        _txtSearch.TextChanged += OnTextChanged;
        _txtSearch.KeyDown += OnKeyDown;

        // Results dropdown
        _lstResults = new ListBox
        {
            Dock = DockStyle.Fill,
            Visible = false,
            BackColor = Color.FromArgb(30, 30, 48),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f),
            BorderStyle = BorderStyle.None,
            IntegralHeight = false,
            MaximumSize = new Size(9999, 200),
        };
        _lstResults.DoubleClick += OnResultSelected;
        _lstResults.KeyDown += OnResultKeyDown;

        // Debounce timer (300ms)
        _debounceTimer = new System.Windows.Forms.Timer { Interval = 300 };
        _debounceTimer.Tick += OnDebounceSearch;

        Height = 230;
        Controls.Add(_lstResults);
        Controls.Add(_txtSearch);
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

    private void OnTextChanged(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void OnDebounceSearch(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        var query = _txtSearch.Text.Trim();

        if (query.Length < 2)
        {
            _lstResults.Visible = false;
            return;
        }

        var results = _instrumentService.Search(query, 15);
        _lstResults.Items.Clear();

        if (results.Count == 0)
        {
            _lstResults.Visible = false;
            return;
        }

        foreach (var r in results)
        {
            _lstResults.Items.Add(r);
        }

        _lstResults.DisplayMember = "DisplayText";
        _lstResults.Visible = true;
        _lstResults.Height = Math.Min(results.Count * 22, 200);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down && _lstResults.Visible && _lstResults.Items.Count > 0)
        {
            _lstResults.Focus();
            _lstResults.SelectedIndex = 0;
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            _lstResults.Visible = false;
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Enter && _lstResults.Visible && _lstResults.SelectedItem != null)
        {
            SelectCurrent();
            e.Handled = true;
        }
    }

    private void OnResultKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            SelectCurrent();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            _lstResults.Visible = false;
            _txtSearch.Focus();
            e.Handled = true;
        }
    }

    private void OnResultSelected(object? sender, EventArgs e) => SelectCurrent();

    private void SelectCurrent()
    {
        if (_lstResults.SelectedItem is SymbolSearchResult result)
        {
            _selectedInstrument = _instrumentService.GetByToken(result.Token);
            _txtSearch.Text = result.Symbol;
            _lstResults.Visible = false;
            _txtSearch.Focus();

            if (_selectedInstrument != null)
                InstrumentSelected?.Invoke(this, _selectedInstrument);
        }
    }

    /// <summary>Programmatically set the search text.</summary>
    public void SetSymbol(string symbol)
    {
        _txtSearch.Text = symbol;
        _lstResults.Visible = false;
    }

    /// <summary>Clear selection.</summary>
    public void Clear()
    {
        _txtSearch.Text = "";
        _selectedInstrument = null;
        _lstResults.Visible = false;
    }
}
