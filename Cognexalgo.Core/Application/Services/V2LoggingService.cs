using System;
using System.IO;
using Serilog;
using Serilog.Events;
using ILogger = Serilog.ILogger;

namespace Cognexalgo.Core.Application.Services
{
    /// <summary>
    /// V2 Structured Logging Service (Module 10):
    /// - Serilog with structured properties for queryable logs
    /// - Rolling daily file sink (database/logs/v2-YYYYMMDD.log)
    /// - Separate signal log file for audit
    /// - Console sink for debugging
    /// - Enriched with StrategyId, Component, TradingMode context
    /// </summary>
    public class V2LoggingService
    {
        private readonly ILogger _logger;
        private readonly ILogger _signalLogger;

        /// <summary>Event for UI forwarding (level, component, message).</summary>
        public event Action<string, string, string>? OnLog;

        public V2LoggingService(string? basePath = null)
        {
            string logDir = basePath ?? Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "database", "logs");
            
            Directory.CreateDirectory(logDir);

            // ─── Main Logger ─────────────────────────────────────
            _logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.WithProperty("Application", "CognexAlgo")
                .Enrich.WithProperty("Version", "2.0")
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{Component}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    path: Path.Combine(logDir, "v2-.log"),
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{Component}] [{StrategyId}] {Message:lj}{NewLine}{Exception}",
                    retainedFileCountLimit: 30,
                    fileSizeLimitBytes: 50 * 1024 * 1024) // 50MB per file
                .CreateLogger();

            // ─── Signal Audit Logger (separate file) ─────────────
            _signalLogger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    path: Path.Combine(logDir, "signals-.log"),
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} | {Message:lj}{NewLine}",
                    retainedFileCountLimit: 90) // 90 days of signal history
                .CreateLogger();
        }

        // ─── Core Logging Methods ────────────────────────────────

        public void Info(string component, string message, string? strategyId = null)
        {
            _logger.ForContext("Component", component)
                   .ForContext("StrategyId", strategyId ?? "-")
                   .Information(message);
            OnLog?.Invoke("INFO", component, message);
        }

        public void Warn(string component, string message, string? strategyId = null)
        {
            _logger.ForContext("Component", component)
                   .ForContext("StrategyId", strategyId ?? "-")
                   .Warning(message);
            OnLog?.Invoke("WARN", component, message);
        }

        public void Error(string component, string message, Exception? ex = null, string? strategyId = null)
        {
            _logger.ForContext("Component", component)
                   .ForContext("StrategyId", strategyId ?? "-")
                   .Error(ex, message);
            OnLog?.Invoke("ERROR", component, message);
        }

        public void Debug(string component, string message, string? strategyId = null)
        {
            _logger.ForContext("Component", component)
                   .ForContext("StrategyId", strategyId ?? "-")
                   .Debug(message);
        }

        // ─── Specialized Logging ─────────────────────────────────

        /// <summary>Log a signal to the dedicated signal audit log.</summary>
        public void LogSignal(string strategyId, string strategyName, string signalType,
                              string symbol, decimal price, string triggerCondition)
        {
            _signalLogger.Information(
                "SIGNAL | Strategy={StrategyId} ({StrategyName}) | {SignalType} | {Symbol} @ {Price} | Trigger: {TriggerCondition}",
                strategyId, strategyName, signalType, symbol, price, triggerCondition);

            Info("Signal", $"{strategyName} → {signalType} on {symbol} @ {price}", strategyId);
        }

        /// <summary>Log an order execution to both main and signal logs.</summary>
        public void LogOrder(string strategyId, string orderId, string symbol,
                            string direction, int qty, decimal price, string status)
        {
            _signalLogger.Information(
                "ORDER  | {OrderId} | {Direction} {Qty} {Symbol} @ {Price} | Status: {Status} | Strategy: {StrategyId}",
                orderId, direction, qty, symbol, price, status, strategyId);

            Info("Order", $"{orderId}: {direction} {qty} {symbol} @ {price} [{status}]", strategyId);
        }

        /// <summary>Log an RMS breach event.</summary>
        public void LogRmsBreach(string strategyId, string ruleType, decimal currentValue,
                                  decimal threshold)
        {
            _logger.ForContext("Component", "RMS")
                   .ForContext("StrategyId", strategyId)
                   .Warning("RMS BREACH: {RuleType} | Current={CurrentValue:F0} | Threshold={Threshold:F0}",
                            ruleType, currentValue, threshold);

            OnLog?.Invoke("WARN", "RMS", $"⚠ {ruleType} breach on {strategyId}: {currentValue:F0} vs {threshold:F0}");
        }

        /// <summary>Log a PnL snapshot (for daily summary).</summary>
        public void LogPnlSnapshot(string strategyId, decimal pnl, decimal highWatermark)
        {
            _logger.ForContext("Component", "PnL")
                   .ForContext("StrategyId", strategyId)
                   .Information("PnL={Pnl:F2} | HWM={HighWatermark:F2}", pnl, highWatermark);
        }
    }
}
