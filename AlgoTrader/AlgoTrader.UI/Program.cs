using AlgoTrader.Brokers;
using AlgoTrader.Brokers.AngelOne;
using AlgoTrader.Core.Interfaces;
using AlgoTrader.Core.Models;
using AlgoTrader.Data;
using AlgoTrader.Data.EfCore;
using AlgoTrader.Data.Encryption;
using AlgoTrader.Data.LiteDb;
using AlgoTrader.MarketData;
using AlgoTrader.Notify;
using AlgoTrader.OMS;
using AlgoTrader.RMS;
using AlgoTrader.Strategy;
using AlgoTrader.UI.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace AlgoTrader.UI;

static class Program
{
    [STAThread]
    static void Main()
    {
        // ─── Serilog ───
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File("logs/algotrader-.log", rollingInterval: RollingInterval.Day)
            .WriteTo.Sink(new UiLogSink())
            .CreateLogger();

        try
        {
            Log.Information("AlgoTrader starting...");

            // ─── Configuration ───
            var config = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();

            // ─── DI Container ───
            var services = new ServiceCollection();

            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AlgoTrader");

            // Logging — bridge Serilog → Microsoft.Extensions.Logging ILogger<T>
            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddSerilog(Log.Logger, dispose: false);
            });
            services.AddSingleton(Log.Logger);

            // Database
            services.AddSingleton(new DatabaseConfig(appDataPath, "algotrader.db"));
            services.AddSingleton<LiteDbContext>();
            services.AddSingleton<DataBaseManager>();
            services.AddSingleton<IDataBaseManager>(sp => sp.GetRequiredService<DataBaseManager>());
            services.AddSingleton<Data.Repositories.AccountRepository>();
            services.AddSingleton<Data.Repositories.StrategyRepository>();
            services.AddSingleton<Data.Repositories.OrderRepository>();
            services.AddSingleton<Data.Repositories.SettingsRepository>();
            services.AddSingleton<CandleRepository>(sp =>
                new CandleRepository(appDataPath, sp.GetRequiredService<ILogger<CandleRepository>>()));
            services.AddSingleton<IEncryptionService, AesEncryptionService>();
            services.AddSingleton<CredentialProtector>();

            // Broker
            services.AddSingleton<IBrokerFactory, BrokerFactory>();

            // Instrument Master
            services.AddSingleton<InstrumentMasterService>(sp =>
                new InstrumentMasterService(appDataPath, sp.GetRequiredService<ILogger<InstrumentMasterService>>()));

            // Market Data
            services.AddSingleton<IMarketDataService, AngelOneMarketDataService>();
            services.AddSingleton<HistoricalDataManager>();
            services.AddSingleton<LiveCandleBuilder>(sp =>
                new LiveCandleBuilder(Core.Enums.TimeFrame.ONE_MINUTE, sp.GetRequiredService<ILogger<LiveCandleBuilder>>()));
            services.AddSingleton<IndicatorEngine>();
            services.AddSingleton<TickDispatcher>();

            // Strategy Engine & Advanced Services
            services.AddSingleton<AdvancedReEntryManager>();
            services.AddSingleton<WaitAndTradeEvaluator>();
            services.AddSingleton<PremiumMatchingValidator>();
            services.AddSingleton<BreakevenSLManager>();
            services.AddSingleton<IndicatorConditionEvaluator>();
            services.AddSingleton<BacktestEngine>();
            services.AddSingleton<PositionTracker>();
            services.AddSingleton<StrategyEngine>();
            services.AddSingleton<IStrategyEngine>(sp => sp.GetRequiredService<StrategyEngine>());
            services.AddSingleton<StrategyEventBus>(sp => sp.GetRequiredService<StrategyEngine>().EventBus);
            services.AddSingleton<AutoSquareOffManager>(sp => sp.GetRequiredService<StrategyEngine>().AutoSquareOff);
            services.AddSingleton<SLManager>();
            // OMS
            services.AddSingleton<IOrderManager, OrderManager>();
            services.AddSingleton<ExecutionEngine>();
            services.AddSingleton<SyncService>();

            // RMS
            services.AddSingleton<IRiskManager, RiskManager>();
            services.AddSingleton<TimeBasedExitService>();

            // Notifications
            var telegramConfig = new TelegramConfig();
            config.GetSection("Telegram").Bind(telegramConfig);
            services.AddSingleton(telegramConfig);
            services.AddSingleton<INotificationService, TelegramNotificationService>();
            services.AddSingleton<InAppAlertService>();

            // UI Forms
            services.AddTransient<MainForm>();
            services.AddTransient<OptionChainForm>();

            var serviceProvider = services.BuildServiceProvider();

            // ─── Initialize DB ───
            var dbManager = serviceProvider.GetRequiredService<IDataBaseManager>();
            dbManager.Initialize();

            // ─── Global Exception Handling ───
            Application.ThreadException += (s, e) =>
            {
                Log.Error(e.Exception, "UI thread exception");
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Log.Fatal((Exception)e.ExceptionObject, "Unhandled exception");
            };

            // ─── Run Application ───
            ApplicationConfiguration.Initialize();
            var mainForm = serviceProvider.GetRequiredService<MainForm>();
            Application.Run(mainForm);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
        }
        finally
        {
            Log.Information("AlgoTrader shutting down...");
            Log.CloseAndFlush();
        }
    }
}