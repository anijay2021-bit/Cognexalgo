using System.Windows;
using Cognexalgo.UI.ViewModels;

namespace Cognexalgo.UI.Views
{
    public partial class PayoffWindow : Window
    {
        public PayoffWindow(PayoffViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}
