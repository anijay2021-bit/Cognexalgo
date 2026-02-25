using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Cognexalgo.Core.Application.Services;
using Cognexalgo.Core.Domain.Entities;
using Cognexalgo.Core.Domain.Enums;
using Cognexalgo.Core.Infrastructure.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cognexalgo.UI.ViewModels
{
    public partial class EnterpriseDashboardViewModel : ObservableObject
    {
        private readonly V2Bridge _v2;
        private readonly Dispatcher _dispatcher;

        [ObservableProperty] private decimal _totalMtm;
        [ObservableProperty] private int _activeStrategiesCount;
        [ObservableProperty] private decimal _dailyMaxLoss;
        [ObservableProperty] private decimal _dailyMaxProfit;
        [ObservableProperty] private string _killSwitchStatus = "OFF";

        public ObservableCollection<LegDisplayMetadata> ActiveLegs { get; } = new();

        public EnterpriseDashboardViewModel(V2Bridge v2)
        {
            _v2 = v2;
            _dispatcher = Dispatcher.CurrentDispatcher;

            // Load initial settings
            DailyMaxLoss = _v2.AccountRms.DailyMaxLoss;
            DailyMaxProfit = _v2.AccountRms.DailyMaxProfit;
            
            // Wire up events
            _v2.AccountRms.OnAccountBreach += (rule, msg) => UpdateAccountStatus();
            _v2.Orchestrator.OnStatusChanged += (id, status) => UpdateStrategyStats();
            
            // Real-time Leg Tracking
            _v2.Simulator.OnOrderFilled += (order) => {
                _dispatcher.Invoke(() => {
                    var leg = ActiveLegs.FirstOrDefault(l => l.Symbol == order.TradingSymbol);
                    if (leg == null) {
                        leg = new LegDisplayMetadata { 
                            Symbol = order.TradingSymbol,
                            Type = order.Direction.ToString(),
                            EntryPrice = order.FilledPrice,
                            Status = "IN_POSITION"
                        };
                        ActiveLegs.Add(leg);
                    } else {
                        leg.Status = "ACTIVE"; // Update if already exists
                    }
                });
            };

            _v2.OnTickProcessed += (tick) => {
                _dispatcher.Invoke(() => {
                    TotalMtm = _v2.AccountRms.TotalDailyPnl;
                    ActiveStrategiesCount = _v2.Orchestrator.ActiveCount;

                    foreach (var leg in ActiveLegs) {
                        if (leg.Symbol.Contains("NIFTY")) {
                            leg.Ltp = tick.NiftyLtp;
                        } else if (leg.Symbol.Contains("BANKNIFTY")) {
                            leg.Ltp = tick.BankNiftyLtp;
                        }

                        // Basic P&L: (Current Price - Buy Price) * Typical Qty (50 for Nifty)
                        // If Sell: (Sell Price - Current Price) * Qty
                        decimal qty = 50; 
                        if (leg.Type == "BUY")
                            leg.Pnl = (leg.Ltp - leg.EntryPrice) * qty;
                        else
                            leg.Pnl = (leg.EntryPrice - leg.Ltp) * qty;
                    }
                });
            };

            // Initialization
            UpdateStrategyStats();
        }

        [RelayCommand]
        private void UpdateRiskSettings()
        {
            _v2.AccountRms.DailyMaxLoss = DailyMaxLoss;
            _v2.AccountRms.DailyMaxProfit = DailyMaxProfit;
            _v2.Logger.Info("UI", $"Updated Global Risk: MaxLoss={DailyMaxLoss}, MaxProfit={DailyMaxProfit}");
        }

        [RelayCommand]
        private void ExitAll()
        {
            _v2.KillAll();
            KillSwitchStatus = "ACTIVATED";
            _v2.Logger.Warn("UI", "CRITICAL: EXIT ALL triggered from Enterprise Dashboard");
        }

        [RelayCommand]
        private async Task ForceTestSignal()
        {
            // Simulate a "Test Signal" -> "Test Order" flow
            var order = new Order
            {
                OrderId = "TEST-" + Guid.NewGuid().ToString().Substring(0, 8),
                TradingSymbol = "NIFTY-MAR-2026-24000-CE",
                Direction = Direction.BUY,
                Quantity = 50,
                Status = OrderStatus.PENDING,
                TradingMode = TradingMode.PaperTrade
            };

            // Trigger fill via Simulator (this will fire the OnOrderFilled event we are subbed to)
            await _v2.Simulator.ExecuteAsync(order, 24000);
            
            _v2.Logger.Info("UI", "Test Signal Forced: " + order.TradingSymbol);
        }

        private void UpdateStrategyStats()
        {
            _dispatcher.Invoke(() => {
                ActiveStrategiesCount = _v2.Orchestrator.ActiveCount;
                // Refresh legs grid logic here
            });
        }

        private void UpdateAccountStatus()
        {
            _dispatcher.Invoke(() => {
                TotalMtm = _v2.AccountRms.TotalDailyPnl; 
                if (_v2.AccountRms.IsKillSwitchActive) KillSwitchStatus = "ACTIVATED";
            });
        }
    }

    public partial class LegDisplayMetadata : ObservableObject
    {
        [ObservableProperty] private string _symbol;
        [ObservableProperty] private string _type;
        [ObservableProperty] private decimal _entryPrice;
        [ObservableProperty] private decimal _ltp;
        [ObservableProperty] private decimal _pnl;
        [ObservableProperty] private string _status;
    }
}
