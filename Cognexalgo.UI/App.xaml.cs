using System.Configuration;
using System.Data;
using System.Windows;
using Cognexalgo.Core.CloudServices;

namespace Cognexalgo.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
    public partial class App : Application
    {
        private Cognexalgo.Core.CloudServices.FirebaseService _firebaseService;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

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
            await System.Threading.Tasks.Task.Delay(3000); 

            splash.Close();

            ShowLoginWindow();
        }

        private void ShowLoginWindow()
        {
            _firebaseService = new Cognexalgo.Core.CloudServices.FirebaseService();
            var loginViewModel = new ViewModels.LoginViewModel(_firebaseService, OnLoginSuccess);
            var loginWindow = new Views.LoginWindow(loginViewModel);
            loginWindow.Show();
        }

        private async void OnLoginSuccess()
        {
            try 
            {
                var engine = new Cognexalgo.Core.TradingEngine();
                await engine.InitializeDatabaseAsync(); // [NEW] Ensure DB tables are created
                
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

