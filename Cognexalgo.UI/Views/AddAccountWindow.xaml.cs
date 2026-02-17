using System.Windows;
using Cognexalgo.UI.ViewModels;

namespace Cognexalgo.UI.Views
{
    public partial class AddAccountWindow : Window
    {
        public AddAccountWindow(AddAccountViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        public void CloseWithSuccess()
        {
            DialogResult = true;
            Close();
        }
    }
}
