using System.Windows;

namespace Cognexalgo.UI
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new PreviewViewModel();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // No-op for preview
        }
    }
}
