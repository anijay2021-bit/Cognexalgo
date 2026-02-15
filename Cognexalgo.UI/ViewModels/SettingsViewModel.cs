using System.Windows;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cognexalgo.Core;
using Cognexalgo.Core.Models;

namespace Cognexalgo.UI.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly TradingEngine _engine;
        private Window _window;

        [ObservableProperty]
        private string _apiKey;

        [ObservableProperty]
        private string _clientCode;

        [ObservableProperty]
        private string _password;

        [ObservableProperty]
        private string _totpKey;

        public SettingsViewModel(TradingEngine engine, Window window)
        {
            _engine = engine;
            _window = window;
            _ = LoadCredentials();
        }

        private async Task LoadCredentials()
        {
            var creds = await _engine.CredentialsRepository.GetAsync();
            if (creds != null)
            {
                ApiKey = creds.ApiKey;
                ClientCode = creds.ClientCode;
                Password = creds.Password;
                TotpKey = creds.TotpKey;
            }
        }

        [RelayCommand]
        public async Task Save()
        {
            if (string.IsNullOrWhiteSpace(ApiKey) || string.IsNullOrWhiteSpace(ClientCode))
            {
                MessageBox.Show("Please enter API Key and Client Code", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var creds = new BrokerCredentials
            {
                ApiKey = ApiKey,
                ClientCode = ClientCode,
                Password = Password,
                TotpKey = TotpKey,
                BrokerName = "AngelOne"
            };

            await _engine.CredentialsRepository.SaveAsync(creds);
            MessageBox.Show("Credentials Saved Successfully!", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
            _window.Close();
        }

        [RelayCommand]
        public void Cancel()
        {
            _window.Close();
        }
    }
}
