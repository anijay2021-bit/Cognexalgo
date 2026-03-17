using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Cognexalgo.UI.ViewModels;

namespace Cognexalgo.UI.Views
{
    public partial class DarkPayoffWindow : Window
    {
        public static readonly string[] ActionOptions = { "BUY", "SELL" };
        public static readonly string[] TypeOptions   = { "CE",  "PE"   };

        private readonly PayoffBuilderViewModel _vm;

        public DarkPayoffWindow(PayoffBuilderViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;

            // Refresh chart whenever payoff data changes
            vm.ProfitData.CollectionChanged += (_, _) => Dispatcher.Invoke(RefreshChart);
            vm.LossData.CollectionChanged   += (_, _) => Dispatcher.Invoke(RefreshChart);

            Loaded += (_, _) => RefreshChart();
        }

        // Shared SelectionChanged handler for Neutral/Bullish/Bearish ListBoxes.
        private void OnStrategySelected(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not PayoffBuilderViewModel vm) return;
            if (sender is not ListBox lb)                     return;
            if (lb.SelectedItem is not string name)           return;

            vm.SelectTemplateCommand.Execute(name);
            lb.SelectedItem = null;
        }

        // Re-render when the chart border is resized so the bitmap matches the container
        private void OnChartSizeChanged(object sender, SizeChangedEventArgs e) => RefreshChart();

        // ── ScottPlot headless rendering → WPF Image ───────────────────────────
        private void RefreshChart()
        {
            int w = Math.Max(100, (int)ChartImage.ActualWidth);
            int h = Math.Max(80,  (int)ChartImage.ActualHeight);

            // Fall back to the parent border size if Image hasn't measured yet
            if (w <= 100 && h <= 80)
            {
                w = 900;
                h = 300;
            }

            var plt = new ScottPlot.Plot();

            // Dark backgrounds
            plt.FigureBackground.Color = ScottPlot.Color.FromHex("#0F1923");
            plt.DataBackground.Color   = ScottPlot.Color.FromHex("#1B2738");
            plt.Grid.MajorLineColor    = ScottPlot.Color.FromHex("#2D3F57");
            plt.Grid.MinorLineColor    = ScottPlot.Color.FromHex("#1E2D3E");

            var tickColor = ScottPlot.Color.FromHex("#7B8FA6");
            plt.Axes.Bottom.TickLabelStyle.ForeColor = tickColor;
            plt.Axes.Left.TickLabelStyle.ForeColor   = tickColor;
            plt.Axes.Bottom.FrameLineStyle.Color     = ScottPlot.Color.FromHex("#2D3F57");
            plt.Axes.Left.FrameLineStyle.Color       = ScottPlot.Color.FromHex("#2D3F57");

            int count = Math.Min(_vm.ProfitData.Count, _vm.LossData.Count);
            if (count >= 2)
            {
                double[] xs = _vm.ProfitData.Take(count).Select(p => p.Price).ToArray();
                double[] ys = new double[count];
                for (int i = 0; i < count; i++)
                    ys[i] = _vm.ProfitData[i].Pnl + _vm.LossData[i].Pnl;

                // Main payoff line — teal
                var line = plt.Add.Scatter(xs, ys);
                line.Color      = ScottPlot.Color.FromHex("#00C9A7");
                line.LineWidth  = 2.5f;
                line.MarkerSize = 0;

                // Zero reference
                var zero = plt.Add.HorizontalLine(0);
                zero.Color       = ScottPlot.Color.FromHex("#7B8FA6");
                zero.LineWidth   = 1;
                zero.LinePattern = ScottPlot.LinePattern.Dashed;

                // Spot price
                if (_vm.SpotPrice > 0)
                {
                    var spot = plt.Add.VerticalLine(_vm.SpotPrice);
                    spot.Color       = ScottPlot.Color.FromHex("#FFD166");
                    spot.LineWidth   = 1.5f;
                    spot.LinePattern = ScottPlot.LinePattern.Dashed;
                }
            }

            plt.Axes.Bottom.Label.Text      = "Underlying Price";
            plt.Axes.Bottom.Label.ForeColor = tickColor;
            plt.Axes.Left.Label.Text        = "P&L (₹)";
            plt.Axes.Left.Label.ForeColor   = tickColor;

            // Render to PNG bytes → WPF BitmapImage
            byte[] png = plt.GetImage(w, h).GetImageBytes();

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption  = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(png);
            bmp.EndInit();
            bmp.Freeze();

            ChartImage.Source = bmp;
        }
    }
}
