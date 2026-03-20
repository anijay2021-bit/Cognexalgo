using System.Windows.Controls;
using Cognexalgo.UI.ViewModels;

namespace Cognexalgo.UI.Views.VCP
{
    public partial class VCPScannerView : UserControl
    {
        private readonly VCPScannerViewModel _viewModel;

        public VCPScannerView(VCPScannerViewModel viewModel)
        {
            InitializeComponent();
            _viewModel  = viewModel;
            DataContext = viewModel;
            Unloaded   += (_, _) => _viewModel.Dispose();
        }

        /// <summary>
        /// Parameterless constructor for XAML / DataTemplate instantiation.
        /// DataContext is set by WPF binding; Dispose is wired on first DataContextChanged.
        /// </summary>
        public VCPScannerView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) =>
            {
                if (DataContext is VCPScannerViewModel vm)
                    Unloaded += (_, _) => vm.Dispose();
            };
        }
    }
}
