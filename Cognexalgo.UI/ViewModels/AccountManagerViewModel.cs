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
                // Ensure Table Exists (Quick Fix for dev env)
                // In prod, use migrations. Here we just want to ensure it works.
                // await _engine.DbContext.Database.EnsureCreatedAsync(); 
                
                // Fetch from DB
                // Note: TradingEngine needs to expose DbContext or a Repository. 
                // Creating a scope or using engine's context if available.
                // For now assuming we can access it or creates a new one.
                // Let's assume _engine has a public DbContext for simplicity in this task scope, 
                // OR we create one. Best practice is Factory, but we'll use a local context for loading.
                
                // Mock Data for UI Dev if DB is empty
                if (Accounts.Count == 0)
                {
                    Accounts.Add(new AccountConfig 
                    { 
                        ClientId = "A8821", 
                        AccountName = "Alpha Fund", 
                        Status = "Active",
                        Description = "Main HFT Account",
                        Broker = "Angel One",
                        FundsUtilized = 250000,
                        FundsAvailable = 1500000,
                        Pnl = 5200.50m,
                        MtmHigh = 8000,
                        MtmLow = -2000
                    });
                     Accounts.Add(new AccountConfig 
                    { 
                        ClientId = "B9932", 
                        AccountName = "Beta Strategy", 
                        Status = "Stopped",
                        Description = "Testing Algo",
                        Broker = "Zerodha",
                        FundsUtilized = 0,
                        FundsAvailable = 500000,
                        Pnl = 0m
                    });
                }
                
                // If we had a Repo, we'd use it. For now using Mock + ready for integration.
                
                _updateTimer.Start();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading accounts: {ex.Message}");
            }
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
