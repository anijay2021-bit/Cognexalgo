using System;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cognexalgo.Core.CloudServices;
using Cognexalgo.Core.Models;

namespace Cognexalgo.UI.ViewModels
{
    public partial class LoginViewModel : ObservableObject
    {
        private readonly IFirebaseService _firebaseService;
        private readonly Action _onLoginSuccess;

        [ObservableProperty]
        private string _licenseKey;

        [ObservableProperty]
        private string _statusMessage;

        [ObservableProperty]
        private bool _isBusy;

        public LoginViewModel(IFirebaseService firebaseService, Action onLoginSuccess)
        {
            _firebaseService = firebaseService;
            _onLoginSuccess = onLoginSuccess;
            // Load License Key from Settings if available (TODO)
             _licenseKey = "OFFLINE"; 
        }

        [RelayCommand]
        public async Task Login()
        {
            if (string.IsNullOrWhiteSpace(LicenseKey))
            {
                StatusMessage = "Please enter a License Key.";
                return;
            }

            // BYPASS FOR LOCAL MODEL
            if (LicenseKey.ToUpper() == "OFFLINE")
            {
                StatusMessage = "Offline Mode Activated...";
                IsBusy = true;
                await Task.Delay(500);
                IsBusy = false;
                _onLoginSuccess?.Invoke();
                return;
            }

            IsBusy = true;
            StatusMessage = "Connecting to Cloud...";

            // Connect First (In a real app, connection parameters might be obfuscated or fetched securely)
            // For now, using placeholders. You'll need to replace these with REAL Firebase credentials.
            bool connected = await _firebaseService.ConnectAsync("https://YOUR-FIREBASE-URL.firebaseio.com/", "YOUR-FIREBASE-SECRET"); 
            
            if (!connected)
            {
                StatusMessage = "Failed to connect to Cloud Server. Try 'OFFLINE' key.";
                IsBusy = false;
                return;
            }

            StatusMessage = "Verifying License...";
            var result = await _firebaseService.ValidateLicenseAsync(LicenseKey);

            IsBusy = false;

            if (result.IsActive)
            {
                StatusMessage = "Success! Access Granted.";
                await Task.Delay(500); // Small delay for UX
                _onLoginSuccess?.Invoke();
            }
            else
            {
                StatusMessage = $"Access Denied: {result.Message}";
            }
        }

        [RelayCommand]
        public void Exit()
        {
            Application.Current.Shutdown();
        }
    }
}
