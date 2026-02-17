using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cognexalgo.Core;
using Cognexalgo.Core.Models;
using Microsoft.EntityFrameworkCore;
using System.Windows;

namespace Cognexalgo.UI.ViewModels
{
    public partial class AccountManagerViewModel : ObservableObject
    {
        private readonly TradingEngine _engine;
        private readonly DispatcherTimer _updateTimer;

        [ObservableProperty]
        private bool _isAdminMode = false; // Hidden by default

        public ObservableCollection<AccountConfig> Accounts { get; } = new ObservableCollection<AccountConfig>();

        public AccountManagerViewModel(TradingEngine engine)
        {
            _engine = engine;

            // Initialize Timer (1 Second Interval)
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _updateTimer.Tick += OnTimerTick;

            // Load Data
            _ = LoadAccountsAsync();
        }

        private async Task LoadAccountsAsync()
        {
            try
            {
                // Fetch from DB using EF Core Context from Engine
                if (_engine.MetadataContext != null)
                {
                    var dbAccounts = await _engine.MetadataContext.AccountConfigs.ToListAsync();
                    
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        Accounts.Clear();
                        foreach (var acc in dbAccounts)
                        {
                            Accounts.Add(acc);
                        }
                    });
                }
                
                _updateTimer.Start();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading accounts: {ex.Message}");
            }
        }

        [RelayCommand]
        public void OpenAddAccount()
        {
            var vm = new AddAccountViewModel(_engine, async () => await LoadAccountsAsync());
            var win = new Views.AddAccountWindow(vm);
            
            vm.CloseAction = () => win.Close();
            
            win.Owner = System.Windows.Application.Current.MainWindow;
            win.ShowDialog();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            // Real-time Update Loop
            // In a real scenario, this would iterate accounts and fetch API snapshots.
            // For now, we simulate "Live" data changes for the UI.
            
            var rnd = new Random();

            foreach (var acc in Accounts)
            {
                if (acc.IsEnabled && acc.Status == "Active")
                {
                    // Update Feed Indicator
                    acc.IsFeedActive = !acc.IsFeedActive; // Blink effect logic or check connection
                    acc.FeedStatusColor = acc.IsFeedActive ? "#2ECC71" : "#E74C3C";

                    // Simulate PNL Fluctuation
                    decimal change = (decimal)(rnd.NextDouble() * 50 - 25);
                    acc.Pnl += change;

                    // Update High/Low
                    if (acc.Pnl > acc.MtmHigh) acc.MtmHigh = acc.Pnl;
                    if (acc.Pnl < acc.MtmLow) acc.MtmLow = acc.Pnl;

                    // Simulate Funds changes (e.g. margin used changes with PNL)
                    acc.FundsUtilized += change * 0.1m; 
                    acc.FundsAvailable -= change * 0.1m;
                }
                else
                {
                    acc.FeedStatusColor = "#95A5A6"; // Gray
                }
            }
        }

        [RelayCommand]
        public async Task ToggleAccount(AccountConfig account)
        {
            // Save state to DB
            account.Status = account.IsEnabled ? "Active" : "Disabled";
            // await SaveToDb(account); 
        }
    }
}
