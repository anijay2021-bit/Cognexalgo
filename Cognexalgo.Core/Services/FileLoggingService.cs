using System;
using System.IO;

namespace Cognexalgo.Core.Services
{
    public class FileLoggingService
    {
        private readonly string _basePath;
        private string _todayFolder;
        private string _logFile;
        private string _signalFile;
        private string _positionFile;

        // Custom Event for UI to subscribe to
        public event Action<string, string, string> OnLog;

        public FileLoggingService()
        {
            // Base path: AppDirectory/database/logs
            _basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "database", "logs");
            InitializeDailyFolder();
        }

        private void InitializeDailyFolder()
        {
            try 
            {
                string dateStr = DateTime.Now.ToString("dd-MM-yyyy");
                _todayFolder = Path.Combine(_basePath, dateStr);

                if (!Directory.Exists(_todayFolder))
                {
                    Directory.CreateDirectory(_todayFolder);
                }

                _logFile = Path.Combine(_todayFolder, "logs.txt");
                _signalFile = Path.Combine(_todayFolder, "signals.txt");
                _positionFile = Path.Combine(_todayFolder, "positions.txt");

                Log("System", "Logging Service Initialized for " + dateStr);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRITICAL] Failed to initialize logs: {ex.Message}");
            }
        }

        public void Log(string component, string message, string level = "INFO")
        {
            string logEntry = $"[{DateTime.Now:HH:mm:ss}] [{level}] [{component}] {message}";
            WriteToFile(_logFile, logEntry);

            // Notify Subscribers (UI)
            OnLog?.Invoke(level, component, message);
        }

        public void LogSignal(string strategy, string symbol, string signal, double price)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] STRATEGY: {strategy} | SYMBOL: {symbol} | SIGNAL: {signal} | PRICE: {price}";
            WriteToFile(_signalFile, line);
            // Also log to main log for audit
            Log("Signal", $"{strategy} triggered {signal} on {symbol}", "INFO");
        }

        public void LogPosition(string symbol, string action, int qty, double price, string orderId)
        {
             string line = $"[{DateTime.Now:HH:mm:ss}] ORDER: {orderId} | {action} {qty} {symbol} @ {price}";
             WriteToFile(_positionFile, line);
             Log("Order", $"Executed {action} for {symbol}", "INFO");
        }

        private void WriteToFile(string filePath, string content)
        {
            try
            {
                // Simple append, thread-safe enough for low volume, ideally utilize a lock or queue for high freq
                lock(this) 
                {
                    File.AppendAllText(filePath, content + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Log Error] {ex.Message}");
            }
        }
    }
}
