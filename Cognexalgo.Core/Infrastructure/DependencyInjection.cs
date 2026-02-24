using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Cognexalgo.Core.Application.Interfaces;
using Cognexalgo.Core.Application.Services;
using Cognexalgo.Core.Infrastructure.Persistence;
using Cognexalgo.Core.Infrastructure.Services;
using Cognexalgo.Core.Services;

namespace Cognexalgo.Core.Infrastructure
{
    /// <summary>
    /// Registers all Core services into the DI container.
    /// Called from App.xaml.cs during startup.
    /// </summary>
    public static class DependencyInjection
    {
        /// <summary>
        /// Adds Cognexalgo Core services to the service collection.
        /// </summary>
        public static IServiceCollection AddCognexCore(
            this IServiceCollection services,
            string? supabaseConnectionString = null,
            bool useSqliteFallback = false)
        {
            // ─── Database ─────────────────────────────────────────
            if (!string.IsNullOrEmpty(supabaseConnectionString) && !useSqliteFallback)
            {
                services.AddDbContext<AppDbContext>(options =>
                    options.UseNpgsql(supabaseConnectionString, npgsqlOptions =>
                    {
                        npgsqlOptions.EnableRetryOnFailure(3);
                        npgsqlOptions.CommandTimeout(30);
                    }));
            }
            else
            {
                // SQLite fallback for offline/dev
                var dbPath = System.IO.Path.Combine(
                    System.AppDomain.CurrentDomain.BaseDirectory, "cognex_v2.db");
                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlite($"Data Source={dbPath}"));
            }

            // ─── ID Generators ────────────────────────────────────
            services.AddScoped<IStrategyIdGenerator, StrategyIdGenerator>();
            services.AddScoped<IOrderIdGenerator, OrderIdGenerator>();
            services.AddScoped<ILegIdGenerator, LegIdGenerator>();

            // ─── Repositories ─────────────────────────────────────
            services.AddScoped<IStrategyRepository, StrategyRepository>();
            services.AddScoped<IOrderRepository, OrderRepository>();
            services.AddScoped<ISignalRepository, SignalRepository>();
            services.AddScoped<ITradeRepository, TradeRepository>();
            services.AddScoped<IInstrumentRepository, InstrumentRepository>();
            services.AddScoped<ICredentialRepository, CredentialRepository>();

            // ─── Broker Adapter ───────────────────────────────────
            services.AddSingleton<SmartApiClient>();
            services.AddSingleton<IAngelOneAdapter, SmartApiClientAdapter>();

            // ─── Application Services ─────────────────────────────
            services.AddScoped<OrderFactory>();
            services.AddScoped<PaperTradeSimulator>();
            services.AddScoped<SignalEngine>();

            // ─── Strategy management (Singletons — live across scopes) ─
            services.AddSingleton<StrategyOrchestrator>();
            services.AddSingleton<StrategyRmsService>();
            services.AddSingleton<AccountRmsService>();

            // ─── Logging & Notifications (Module 10) ──────────────
            services.AddSingleton<V2LoggingService>();
            services.AddSingleton<TelegramNotifier>(sp => 
                new TelegramNotifier("", "", false)); // Configured via V2Bridge
            services.AddSingleton<WindowsToastNotifier>();

            return services;
        }
    }
}
