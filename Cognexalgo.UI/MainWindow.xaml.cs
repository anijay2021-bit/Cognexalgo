using System.Windows;

namespace Cognexalgo.UI
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
             if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;
            else
                WindowState = WindowState.Maximized;
        }


        private async void ExitPosition_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as System.Windows.Controls.Button;
            if (button?.Tag is Cognexalgo.Core.Models.Position position)
            {
                // Show confirmation dialog
                var result = System.Windows.MessageBox.Show(
                    $"Are you sure you want to exit this position?\n\n" +
                    $"Symbol: {position.TradingSymbol}\n" +
                    $"Quantity: {position.NetQty}\n" +
                    $"Current P&L: ₹{position.Pnl:N2}",
                    "Confirm Exit Position",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    // Call ViewModel to place exit order
                    var viewModel = DataContext as ViewModels.MainViewModel;
                    if (viewModel != null)
                    {
                        bool success = await viewModel.ExitPosition(position);
                        
                        if (success)
                        {
                            System.Windows.MessageBox.Show(
                                $"Exit order placed successfully for {position.TradingSymbol}\n\n" +
                                $"Check the Logs tab for order details.",
                                "Exit Successful",
                                System.Windows.MessageBoxButton.OK,
                                System.Windows.MessageBoxImage.Information);
                        }
                        else
                        {
                            System.Windows.MessageBox.Show(
                                $"Failed to place exit order for {position.TradingSymbol}\n\n" +
                                $"Check the Logs tab for error details.",
                                "Exit Failed",
                                System.Windows.MessageBoxButton.OK,
                                System.Windows.MessageBoxImage.Error);
                        }
                    }
                    else
                    {
                        System.Windows.MessageBox.Show(
                            "Error: Could not access trading engine.",
                            "Error",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Error);
                    }
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private bool _isSafeExitComplete = false;

        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isSafeExitComplete) return;

            // 1. Cancel immediate close
            e.Cancel = true;

            // 2. Sync overlay removed (Design2 uses no named overlay element)

            // 3. Run Safe Exit Logic
            if (DataContext is ViewModels.MainViewModel vm)
            {
                vm.SaveSettings();
                
                // Get SafeExitService from VM
                if (vm.SafeExitService != null)
                {
                    bool success = await vm.SafeExitService.ExecuteSafeExitAsync();
                    
                    if (!success)
                    {
                        var result = MessageBox.Show(
                            "Safe-Exit Sync Failed! Data or Code might not be backed up.\n\n" +
                            "Do you want to retry?",
                            "Sync Failed",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                        if (result == MessageBoxResult.Yes)
                        {
                            return; // User cancelled exit to retry
                        }
                    }
                }
            }

            // 4. Force Shutdown after sync
            _isSafeExitComplete = true;
            Application.Current.Shutdown();
        }

        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // SECRET SHORTCUT: Ctrl+Shift+A to toggle Admin Mode (Add Account button)
            if (System.Windows.Input.Keyboard.Modifiers == (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift) &&
                e.Key == System.Windows.Input.Key.A)
            {
                if (DataContext is ViewModels.MainViewModel vm && vm.AccountManager != null)
                {
                    vm.AccountManager.IsAdminMode = !vm.AccountManager.IsAdminMode;
                    
                    if (vm.AccountManager.IsAdminMode)
                    {
                        System.Windows.MessageBox.Show("Developer Mode: ON (Add Account enabled)", "Admin Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Developer Mode: OFF (Add Account hidden)", "Admin Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
        }
    }
}