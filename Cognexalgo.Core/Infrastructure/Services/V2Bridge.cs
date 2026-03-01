using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Cognexalgo.Core.Application.Interfaces;
using Cognexalgo.Core.Application.Services;
using Cognexalgo.Core.Domain.Strategies;
using Cognexalgo.Core.Domain.ValueObjects;
using Cognexalgo.Core.Infrastructure;
using Cognexalgo.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cognexalgo.Core.Infrastructure.Services
{
    /// <summary>
    /// V2 Bridge: Integrates new Clean Architecture services with the existing TradingEngine.
    /// Provides a single entry point for App.xaml.cs or MainViewModel to access V2 capabilities
    /// without rewriting the entire UI layer.
    /// 
    /// Usage:
    ///   var bridge = V2Bridge.Initialize(config);
    ///   bridge.Orchestrator.StartStrategyAsync(myStrategy);
    /// </summary>
    public class V2Bridge : IDisposable
    {
        public IServiceProvider Services { get; private set; }
        public StrategyOrchestrator Orchestrator { get; private set; }
        public StrategyRmsService StrategyRms { get; private set; }
        public AccountRmsService AccountRms { get; private set; }
        public IAngelOneAdapter BrokerAdapter { get; private set; }
        public SignalEngine SignalEngine { get; private set; }
        public PaperTradeSimulator Simulator { get; private set; }
        public V2LoggingService Logger { get; private set; }
        public TelegramNotifier Telegram { get; private set; }
        public WindowsToastNotifier Toast { get; private set; }
        public OrderPollingService OrderPoller { get; private set; }
        // F2: Exposed so MainViewModel can pass to HybridV2Strategy.SetHistoryCacheService
        public Cognexalgo.Core.Services.HistoryCacheService? HistoryCache { get; private set; }

        public bool IsInitialized { get; private set; } = false;
        public event Action<TickContext>? OnTickProcessed;

        // Option chain cache for strategy strike resolution
        public System.Collections.Generic.List<Models.OptionChainItem>? CachedNiftyChain { get; set; }
        public System.Collections.Generic.List<Models.OptionChainItem>? CachedBankNiftyChain { get; set; }

        /// <summary>
        /// Initialize the V2 DI container and all services.
        /// Call this from App.xaml.cs OnLoginSuccess or TradingEngine constructor.
        /// </summary>
        public static async Task<V2Bridge> InitializeAsync(IConfiguration config)
        {
            var bridge = new V2Bridge();

            // ─── Build DI Container ──────────────────────────────
            var services = new ServiceCollection();

            string connStr = config.GetConnectionString("AlgoDatabase") ?? "";
            bool useSqlite = string.Equals(config["V2:UseSqliteFallback"], "true", 
                                           StringComparison.OrdinalIgnoreCase);
            
            services.AddCognexCore(connStr, useSqlite);
            
            bridge.Services = services.BuildServiceProvider();

            // ─── Resolve Singletons ──────────────────────────────
            bridge.Orchestrator = bridge.Services.GetRequiredService<StrategyOrchestrator>();
            bridge.StrategyRms = bridge.Services.GetRequiredService<StrategyRmsService>();
            bridge.AccountRms = bridge.Services.GetRequiredService<AccountRmsService>();
            bridge.BrokerAdapter = bridge.Services.GetRequiredService<IAngelOneAdapter>();
            bridge.SignalEngine = bridge.Services.GetRequiredService<SignalEngine>();
            bridge.Simulator = bridge.Services.GetRequiredService<PaperTradeSimulator>();
            bridge.Logger = bridge.Services.GetRequiredService<V2LoggingService>();
            bridge.Toast = bridge.Services.GetRequiredService<WindowsToastNotifier>();
            // F2: Resolve HistoryCacheService for indicator warm-up
            try { bridge.HistoryCache = bridge.Services.GetRequiredService<Cognexalgo.Core.Services.HistoryCacheService>(); }
            catch { /* optional — V2 runs without indicator conditions if not available */ }

            // ─── Configure Telegram from appsettings ─────────────
            string telegramToken = config["V2:Notifications:TelegramBotToken"] ?? "";
            string telegramChat = config["V2:Notifications:TelegramChatId"] ?? "";
            bool telegramEnabled = string.Equals(config["V2:Notifications:TelegramEnabled"],
                                                  "true", StringComparison.OrdinalIgnoreCase);
            bridge.Telegram = new TelegramNotifier(telegramToken, telegramChat, telegramEnabled);

            bool toastEnabled = !string.Equals(config["V2:Notifications:WindowsToastEnabled"],
                                                "false", StringComparison.OrdinalIgnoreCase);
            bridge.Toast.IsEnabled = toastEnabled;

            // ─── Configure Account RMS from appsettings ──────────
            if (decimal.TryParse(config["V2:AccountRms:DailyMaxLoss"], out var dml))
                bridge.AccountRms.DailyMaxLoss = dml;
            if (decimal.TryParse(config["V2:AccountRms:DailyMaxProfit"], out var dmp))
                bridge.AccountRms.DailyMaxProfit = dmp;
            if (int.TryParse(config["V2:AccountRms:MaxConcurrentStrategies"], out var mcs))
                bridge.AccountRms.MaxConcurrentStrategies = mcs;
            if (decimal.TryParse(config["V2:AccountRms:MaxMarginUtilizationPercent"], out var mmu))
                bridge.AccountRms.MaxMarginUtilizationPercent = mmu;

            // ─── EF Core — Safe Table Creation ─────────────────
            // EnsureCreatedAsync is a no-op when legacy tables exist,
            // so we use GenerateCreateScript + IF NOT EXISTS instead.
            try
            {
                using var scope = bridge.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                string sql = db.Database.GenerateCreateScript();

                using var conn = db.Database.GetDbConnection();
                await conn.OpenAsync();

                var stmts = sql.Split(new[] { ";\r\n", ";\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var stmt in stmts)
                {
                    string s = stmt.Trim();
                    if (string.IsNullOrWhiteSpace(s)) continue;

                    s = s.Replace("CREATE TABLE", "CREATE TABLE IF NOT EXISTS")
                         .Replace("CREATE INDEX", "CREATE INDEX IF NOT EXISTS")
                         .Replace("CREATE UNIQUE INDEX", "CREATE UNIQUE INDEX IF NOT EXISTS");

                    try
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = s;
                        await cmd.ExecuteNonQueryAsync();
                    }
                    catch { /* Table/index may already exist — safe to ignore */ }
                }

                bridge.Logger.Info("Database", "V2 tables ensured (IF NOT EXISTS)");
            }
            catch (Exception ex)
            {
                bridge.Logger.Warn("Database", $"DB init warning: {ex.Message}");
            }

            // ─── Crash Recovery ───────────────────────────────────
            try
            {
                await bridge.Orchestrator.RecoverFromCrashAsync();
            }
            catch (Exception ex)
            {
                bridge.Logger.Warn("V2Bridge", $"Crash recovery warning: {ex.Message}");
            }

            // ─── Wire Orchestrator → Logging + Notifications ─────
            bridge.Orchestrator.OnLog += (level, msg) =>
            {
                switch (level)
                {
                    case "ERROR": bridge.Logger.Error("Orchestrator", msg); break;
                    case "WARN": bridge.Logger.Warn("Orchestrator", msg); break;
                    default: bridge.Logger.Info("Orchestrator", msg); break;
                }
            };

            // ─── Wire RMS → Logging + Telegram + Toast ───────────
            bridge.StrategyRms.OnRmsBreach += (strategyId, ruleType, currentValue) =>
            {
                bridge.Logger.LogRmsBreach(strategyId, ruleType.ToString(), currentValue, 0);
                bridge.Toast.NotifyRmsBreach(ruleType.ToString(), strategyId, currentValue);
                _ = bridge.Telegram.SendRmsBreachAlert(strategyId, ruleType.ToString(), currentValue, 0);
            };

            bridge.AccountRms.OnAccountBreach += (ruleType, message) =>
            {
                bridge.Logger.Warn("AccountRMS", message);
                if (ruleType == Domain.Enums.RmsRuleType.KillSwitch)
                    _ = bridge.Telegram.SendKillSwitchAlert();
            };

            // ─── Wire Signals → Telegram + Toast ──────────────────
            bridge.SignalEngine.OnSignalProcessed += (signal) =>
            {
                _ = bridge.Telegram.SendSignalAlert(
                    signal.StrategyId,
                    signal.SignalType.ToString(),
                    signal.Symbol ?? "",
                    (decimal)signal.Price);
                bridge.Toast.NotifySignal(
                    signal.StrategyId,
                    signal.SignalType.ToString(),
                    signal.Symbol ?? "",
                    (decimal)signal.Price);
            };

            // ─── Wire Order Fills → Telegram + Toast ──────────────
            bridge.SignalEngine.OnOrderGenerated += (order) =>
            {
                _ = bridge.Telegram.SendOrderFillAlert(
                    order.OrderId,
                    order.TradingSymbol,
                    order.Direction.ToString(),
                    order.Quantity,
                    order.FilledPrice);
                bridge.Toast.NotifyOrderFill(
                    order.OrderId,
                    order.TradingSymbol,
                    order.Direction.ToString(),
                    order.FilledPrice);
            };

            // ─── Wire Strategy Status → Telegram ──────────────────
            bridge.Orchestrator.OnStatusChanged += (strategyId, status) =>
            {
                if (status == Domain.Enums.StrategyStatus.Error)
                    _ = bridge.Telegram.SendSignalAlert(strategyId, "STOPPED", "", 0);
            };

            // ─── Order Status Polling (live orders only) ─────────
            bridge.OrderPoller = new OrderPollingService(bridge.BrokerAdapter,
                bridge.Services.GetRequiredService<IOrderRepository>());

            bridge.OrderPoller.OnLog += (level, msg) =>
            {
                switch (level)
                {
                    case "ERROR": bridge.Logger.Error("OrderPoll", msg); break;
                    case "WARN": bridge.Logger.Warn("OrderPoll", msg); break;
                    default: bridge.Logger.Info("OrderPoll", msg); break;
                }
            };

            bridge.OrderPoller.OnOrderFilled += (order) =>
            {
                _ = bridge.Telegram.SendOrderFillAlert(
                    order.OrderId,
                    order.TradingSymbol,
                    order.Direction.ToString(),
                    order.Quantity,
                    order.FilledPrice);
                bridge.Toast.NotifyOrderFill(
                    order.OrderId,
                    order.TradingSymbol,
                    order.Direction.ToString(),
                    order.FilledPrice);
            };

            bridge.OrderPoller.OnOrderRejected += (order) =>
            {
                _ = bridge.Telegram.SendRmsBreachAlert(
                    order.StrategyId,
                    "ORDER_REJECTED",
                    order.Price,
                    0);
            };

            // Auto-track live orders from SignalEngine
            bridge.SignalEngine.OnOrderGenerated += (order) =>
            {
                bridge.OrderPoller.TrackOrder(order);
            };

            // ─── Schedule EOD auto-square-off at 15:20 ───────────
            bridge.ScheduleEodSquareOff();

            bridge.IsInitialized = true;
            bridge.Logger.Info("V2Bridge", "V2 Bridge initialized successfully");
            return bridge;
        }

        /// <summary>
        /// Feed a tick from the existing TickerService to all V2 strategies.
        /// Call this from the OnTick handler in MainViewModel.
        /// </summary>
        public async Task DispatchTickAsync(double niftyLtp, double bankNiftyLtp, 
                                             double finniftyLtp = 0)
        {
            if (!IsInitialized || Orchestrator.ActiveCount == 0) return;

            var tick = new TickContext
            {
                NiftyLtp = (decimal)niftyLtp,
                BankNiftyLtp = (decimal)bankNiftyLtp,
                FinniftyLtp = (decimal)finniftyLtp,
                Timestamp = DateTime.Now,
                NiftyOptionChain = CachedNiftyChain,
                BankNiftyOptionChain = CachedBankNiftyChain
            };

            await Orchestrator.DispatchTickAsync(tick);
            OnTickProcessed?.Invoke(tick);
        }

        /// <summary>
        /// Share V1 TradingEngine's already-authenticated SmartApiClient with V2.
        /// Call this immediately after _engine.ConnectAsync() succeeds so live orders work.
        /// </summary>
        public void SyncBrokerAuth(Cognexalgo.Core.Services.SmartApiClient v1Client)
        {
            if (BrokerAdapter is SmartApiClientAdapter adapter)
                adapter.UseExistingClient(v1Client);
        }

        /// <summary>Kill Switch — immediately stop everything.</summary>
        public void KillAll()
        {
            Orchestrator.StopAll();
            AccountRms.ToggleKillSwitch(true);
        }

        /// <summary>Get a scoped service from the DI container.</summary>
        public T GetService<T>() where T : class
        {
            using var scope = Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<T>();
        }

        // ─── EOD timer ────────────────────────────────────────────
        private System.Threading.Timer? _eodTimer;

        /// <summary>
        /// Schedule EOD auto-square-off at 15:20 today (no-op if already past).
        /// </summary>
        public void ScheduleEodSquareOff()
        {
            var now = DateTime.Now;
            var cutoff = DateTime.Today.AddHours(15).AddMinutes(15);
            if (now >= cutoff)
            {
                Logger.Info("V2Bridge", "EOD square-off: already past 15:15, not scheduling");
                return;
            }

            var delay = cutoff - now;
            _eodTimer = new System.Threading.Timer(_ =>
            {
                Logger.Warn("V2Bridge", "⏰ EOD auto-square-off triggered (15:15)");
                Orchestrator.EodSquareOff();
                _ = Telegram.SendKillSwitchAlert();
                _eodTimer?.Dispose();
            }, null, delay, System.Threading.Timeout.InfiniteTimeSpan);

            Logger.Info("V2Bridge", $"EOD square-off scheduled at {cutoff:HH:mm:ss}");
        }

        public void Dispose()
        {
            _eodTimer?.Dispose();
            OrderPoller?.Dispose();
            if (Services is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
