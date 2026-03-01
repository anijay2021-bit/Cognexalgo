using System.Windows;
using Cognexalgo.UI.ViewModels;

namespace Cognexalgo.UI.Views
{
    public partial class ReportsWindow : Window
    {
        public ReportsWindow(ReportsViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}
