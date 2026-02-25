using System;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cognexalgo.Core.CloudServices;
using Cognexalgo.Core.Models;
using Cognexalgo.Core.Services;

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

        // ─── Pre-Login Data Download Protocol ─────────────────────
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
        private bool _isDataReady = false;

        [ObservableProperty]
        private string _dataStatusMessage = "Preparing data download...";

        [ObservableProperty]
        private double _dataProgress = 0;

        [ObservableProperty]
        private bool _isDownloading = false;

        // Expose pre-loaded services for TradingEngine
        public TokenService? PreLoadedTokenService { get; private set; }
        public AngelOneDataService? PreLoadedDataService { get; private set; }

        public LoginViewModel(IFirebaseService firebaseService, Action onLoginSuccess)
        {
            _firebaseService = firebaseService;
            _onLoginSuccess = onLoginSuccess;
            _licenseKey = "OFFLINE"; 
        }

        /// <summary>
        /// Pre-Login Protocol: Download expiries + historical data.
        /// Called by App.xaml.cs immediately after showing the Login window.
        /// </summary>
        public async Task StartDataDownloadAsync()
        {
            IsDownloading = true;
            IsDataReady = false;

            try
            {
                // ─── Step 1: Download Scrip Master (Expiry Data) ──────
                // NOTE: Scrip Master is a file download, does NOT need API auth
                DataStatusMessage = "📥 Downloading Scrip Master (expiries)...";
                DataProgress = 10;

                var tokenService = new TokenService();
                await tokenService.LoadMasterAsync();

                int symbolCount = tokenService.GetSymbolCount();
                if (symbolCount == 0)
                {
                    DataStatusMessage = "❌ Failed to download Scrip Master. Check internet.";
                    IsDownloading = false;
                    return;
                }

                DataStatusMessage = $"✓ Scrip Master loaded: {symbolCount} symbols";
                DataProgress = 60;
                PreLoadedTokenService = tokenService;

                // ─── Step 2: Prepare DataService (no history yet) ─────────
                // Deep history download requires JWT auth (Angel One login).
                // It will happen AFTER login via BootstrapperService.Step2_FetchHistoricalDataAsync()
                var apiClient = new SmartApiClient();
                var logger = new FileLoggingService();
                var dataService = new AngelOneDataService(apiClient, tokenService, logger);
                PreLoadedDataService = dataService;

                DataStatusMessage = $"✅ Data Ready — {symbolCount} symbols loaded. History will download after login.";
                DataProgress = 100;
                IsDataReady = true;
            }
            catch (Exception ex)
            {
                DataStatusMessage = $"❌ Download failed: {ex.Message}";
                DataProgress = 0;
                IsDataReady = false;
            }
            finally
            {
                IsDownloading = false;
            }
        }

        /// <summary>
        /// Retry download if it failed. Bound to a "RETRY" button on the login screen.
        /// </summary>
        [RelayCommand]
        public async Task RetryDownload()
        {
            await StartDataDownloadAsync();
        }

        private bool CanLogin() => IsDataReady && !IsBusy;

        [RelayCommand(CanExecute = nameof(CanLogin))]
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
                await Task.Delay(500);
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
