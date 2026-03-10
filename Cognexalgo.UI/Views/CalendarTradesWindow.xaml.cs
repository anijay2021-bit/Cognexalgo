using System.Windows;
using Cognexalgo.UI.ViewModels;

namespace Cognexalgo.UI.Views
{
    public partial class CalendarTradesWindow : Window
    {
        public CalendarTradesWindow(CalendarTradesViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
            Closed += (_, __) => vm.StopRefresh();
        }
    }
}
