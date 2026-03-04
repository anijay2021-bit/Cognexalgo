using System.Windows;
using Cognexalgo.UI.ViewModels;

namespace Cognexalgo.UI.Views
{
    public partial class SchedulerWindow : Window
    {
        public SchedulerWindow(SchedulerViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}
