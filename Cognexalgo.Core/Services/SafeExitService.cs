using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cognexalgo.Core.Data;
using Cognexalgo.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Cognexalgo.Core.Services
{
    public class SafeExitService
    {
        private readonly TradingEngine _engine;
        private readonly AlgoDbContext _context;
        private readonly FileLoggingService _logger;

        public SafeExitService(TradingEngine engine, AlgoDbContext context)
        {
            _engine = engine;
            _context = context;
            _logger = engine.Logger;
        }

        public async Task<bool> ExecuteSafeExitAsync()
        {
            try
            {
                _logger.Log("SafeExit", "Initiating Safe-Exit Protocol...");

                // 1. Sync Session Data to DB
                await SyncSessionToDatabaseAsync();

                // 2. Git Auto-Commit
                bool gitSuccess = GitHelper.AutoCommit(_logger);
                
                if (gitSuccess)
                    _logger.Log("SafeExit", "Git Auto-Commit Successful.");
                else
                    _logger.Log("SafeExit", "WARNING: Git Auto-Commit Failed.");

                return true;
            }
            catch (Exception ex)
            {
                _logger.Log("SafeExit", $"CRITICAL ERROR during Safe-Exit: {ex.Message}");
                return false;
            }
        }

        private async Task SyncSessionToDatabaseAsync()
        {
            try
            {
                // 0. Ensure Tables Exist (Self-Healing)
                await EnsureTablesExistAsync();

                var session = new ClientSession
                {
                    Id = Guid.NewGuid(),
                    StartTime = DateTime.Today.AddHours(9).AddMinutes(15), 
                    EndTime = DateTime.Now,
                    TotalPnL = (decimal)_engine.GetTotalPnL(),
                    TradeCount = 0, 
                    StrategiesUsed = "Dynamic/Hybrid",
                    MachineName = Environment.MachineName,
                    Status = "COMPLETED"
                };

                // Capture Trade History from OrderRepository if possible
                // For now, we'll log the session summary
                _context.ClientSessions.Add(session);
                await _context.SaveChangesAsync();
                
                _logger.Log("SafeExit", "Session data synced to Database.");
            }
            catch (Exception ex)
            {
                _logger.Log("SafeExit", $"DB Sync Failed: {ex.Message}");
                // Don't throw, try to continue to Git sync
            }
        }

        private async Task EnsureTablesExistAsync()
        {
            try
            {
                 string createSessionsTable = @"
                     CREATE TABLE IF NOT EXISTS client_sessions (
                         Id UUID PRIMARY KEY,
                         StartTime TIMESTAMP,
                         EndTime TIMESTAMP,
                         TotalPnL NUMERIC,
                         TradeCount INTEGER,
                         StrategiesUsed TEXT,
                         MachineName TEXT,
                         Status TEXT
                     );";
                  
                  string createHistoryTable = @"
                     CREATE TABLE IF NOT EXISTS trade_history (
                         Id UUID PRIMARY KEY,
                         SessionId UUID,
                         Symbol TEXT,
                         Quantity INTEGER,
                         EntryPrice NUMERIC,
                         ExitPrice NUMERIC,
                         PnL NUMERIC,
                         StrategyName TEXT,
                         TransactionType TEXT,
                         EntryTime TIMESTAMP,
                         ExitTime TIMESTAMP
                     );";

                await _context.Database.ExecuteSqlRawAsync(createSessionsTable);
                await _context.Database.ExecuteSqlRawAsync(createHistoryTable);
            }
            catch (Exception ex)
            {
                _logger.Log("SafeExit", $"Table Creation Check Failed: {ex.Message}");
            }
        }
    }

    public static class GitHelper
    {
        public static bool AutoCommit(FileLoggingService logger)
        {
            try
            {
                string workingDir = AppDomain.CurrentDomain.BaseDirectory;
                // Walk up to find .git root (often 3-4 levels up from bin/Debug)
                // Simplified: Assume project root or current dir for now
                // In production, robustly find git root
                
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string commitMsg = $"Closing Sync: {timestamp}";

                RunGitCommand("add .", logger);
                RunGitCommand($"commit -m \"{commitMsg}\"", logger);
                RunGitCommand("push origin main", logger);

                return true;
            }
            catch (Exception ex)
            {
                logger.Log("Git", $"Git Error: {ex.Message}");
                return false;
            }
        }

        private static void RunGitCommand(string args, FileLoggingService logger)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = GetGitRoot() 
                };

                using (var process = Process.Start(psi))
                {
                    process.WaitForExit(5000); // 5s timeout per command
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    
                    if (!string.IsNullOrEmpty(output)) logger.Log("Git", output);
                    if (!string.IsNullOrEmpty(error) && !error.Contains("nothing to commit")) 
                        logger.Log("Git", $"Error/Info: {error}");
                }
            }
            catch (Exception ex)
            {
                logger.Log("Git", $"Command Failed '{args}': {ex.Message}");
            }
        }
        
        private static string GetGitRoot()
        {
             // Traverse up from BaseDirectory to find .git
             var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
             while (dir != null)
             {
                 if (dir.GetDirectories(".git").Any()) return dir.FullName;
                 dir = dir.Parent;
             }
             return AppDomain.CurrentDomain.BaseDirectory; // Fallback
        }
    }
}
