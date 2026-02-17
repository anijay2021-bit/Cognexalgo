using System;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cognexalgo.Core.Models;
using Cognexalgo.Core;

namespace Cognexalgo.UI.ViewModels
{
    public partial class AddAccountViewModel : ObservableObject
    {
        private readonly TradingEngine _engine;
        private readonly Action _onSuccess;

        [ObservableProperty] private string _clientId;
        [ObservableProperty] private string _accountName;
        [ObservableProperty] private string _broker = "Angel One";
        [ObservableProperty] private string _apiKey;
        [ObservableProperty] private string _totpKey;
        [ObservableProperty] private string _description;

        public AddAccountViewModel(TradingEngine engine, Action onSuccess)
        {
            _engine = engine;
            _onSuccess = onSuccess;
        }

        public Action CloseAction { get; set; }

        [RelayCommand]
        public async Task Save()
        {
            if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(AccountName))
            {
                MessageBox.Show("Client ID and Account Name are required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var account = new AccountConfig
                {
                    ClientId = ClientId,
                    AccountName = AccountName,
                    Broker = Broker,
                    Description = Description,
                    IsEnabled = true,
                    Status = "Active"
                };

                // Add to DB
                _engine.MetadataContext.AccountConfigs.Add(account);
                
                // Persist the AccountConfig
                await _engine.MetadataContext.SaveChangesAsync();

                _onSuccess?.Invoke();
                CloseAction?.Invoke();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving account: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void Cancel()
        {
            // Handled by window close usually
        }
    }
}
