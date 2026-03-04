using System.Configuration;
using System.Data;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Cognexalgo.Core.CloudServices;

namespace Cognexalgo.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
    public partial class App : Application
    {
        private Cognexalgo.Core.CloudServices.FirebaseService _firebaseService;
        private ViewModels.LoginViewModel _loginViewModel; // [NEW] Keep reference for pre-loaded services

        protected override async void OnStartup(StartupEventArgs e)
        {
            Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(
                "Ngo9BigBOggjHTQxAR8/V1JGaF1cXmhNYVBpR2NbeU51flBCalhSVAciSV9jS3hTdUVnWXpbdXFQRWJVVU91XQ==");
            base.OnStartup(e);

            // [NEW] Load Configuration
            try 
            {
                var builder = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

                var config = builder.Build();
                Resources.Add("Configuration", config);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading configuration: {ex.Message}", "Config Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            ShutdownMode = ShutdownMode.OnExplicitShutdown; 

            AppDomain.CurrentDomain.UnhandledException += (s, args) => 
                Log("AppDomain Exception: " + args.ExceptionObject.ToString());
            
            DispatcherUnhandledException += (s, args) => 
            {
                Log("Dispatcher Exception: " + args.Exception.ToString());
                args.Handled = true;
            };

            // SPLASH SCREEN
            var splash = new Views.SplashWindow();
            splash.Show();

            // Simulate Initialization
            await System.Threading.Tasks.Task.Delay(2000); 

            splash.Close();

            ShowLoginWindow();
        }

        private void ShowLoginWindow()
        {
            _firebaseService = new Cognexalgo.Core.CloudServices.FirebaseService();
            _loginViewModel = new ViewModels.LoginViewModel(_firebaseService, OnLoginSuccess);
            var loginWindow = new Views.LoginWindow(_loginViewModel);
            loginWindow.Show();

            // [NEW] ─── PRE-LOGIN DATA DOWNLOAD PROTOCOL ───────────────
            // Fire-and-forget: download data in background while user sees login screen.
            // LOGIN button remains disabled until download completes via IsDataReady binding.
            _ = _loginViewModel.StartDataDownloadAsync();
        }

        private async void OnLoginSuccess()
        {
            try 
            {
                // [NEW] Pass pre-loaded services to TradingEngine
                var engine = new Cognexalgo.Core.TradingEngine(
                    preLoadedTokenService: _loginViewModel?.PreLoadedTokenService,
                    preLoadedDataService: _loginViewModel?.PreLoadedDataService);

                await engine.InitializeDatabaseAsync();
                
                engine.SetCloudService(_firebaseService); 
                var mainViewModel = new ViewModels.MainViewModel(engine);
                var mainWindow = new MainWindow { DataContext = mainViewModel };
                
                Application.Current.MainWindow = mainWindow;
                mainWindow.Show();
                
                // Close Login Window
                foreach (Window win in Application.Current.Windows)
                {
                    if (win is Views.LoginWindow) win.Close();
                }

                ShutdownMode = ShutdownMode.OnMainWindowClose;
            }
            catch (Exception ex)
            {
                Log("Login Transition Failed: " + ex.ToString());
            }
        }

        private void Log(string message)
        {
            try
            {
                System.IO.File.AppendAllText("startup_error.txt", DateTime.Now + ": " + message + Environment.NewLine);
                MessageBox.Show(message, "CRASH", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
        }
    }
