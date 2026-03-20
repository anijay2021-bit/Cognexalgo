using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Cognexalgo.Core.Application.Interfaces;
using Cognexalgo.Core.Domain.Patterns;
using Cognexalgo.Core.Models;
using Microsoft.Extensions.Logging;

namespace Cognexalgo.Core.Services.Settings
{
    /// <summary>
    /// JSON-file–backed implementation of <see cref="IVCPSettingsService"/>.
    /// Reads/writes <c>vcp_settings.json</c> in the application base directory.
    /// Atomic writes use a temp file + <see cref="File.Replace"/> to prevent
    /// partial writes from corrupting the settings file.
    /// </summary>
    public sealed class VCPSettingsService : IVCPSettingsService
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented             = true,
            PropertyNameCaseInsensitive = true,
        };

        private readonly string _filePath;
        private readonly string _tempPath;
        private readonly ILogger<VCPSettingsService> _logger;

        public VCPSettingsService(ILogger<VCPSettingsService> logger)
            : this(logger, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vcp_settings.json"))
        {
        }

        // Secondary constructor — lets tests (and future DI overrides) inject a custom file path.
        public VCPSettingsService(ILogger<VCPSettingsService> logger, string filePath)
        {
            _logger   = logger;
            _filePath = filePath;
            _tempPath = Path.ChangeExtension(filePath, ".tmp");
        }

        // ── IVCPSettingsService ───────────────────────────────────────────────

        /// <inheritdoc/>
        public VCPSettings Load()
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation(
                    "[VCPSettings] File not found at '{Path}'. Returning defaults.", _filePath);
                return Validate(Defaults());
            }

            try
            {
                string json = File.ReadAllText(_filePath);
                var settings = JsonSerializer.Deserialize<VCPSettings>(json, _jsonOptions);

                if (settings is null)
                {
                    _logger.LogWarning(
                        "[VCPSettings] Deserialized null from '{Path}'. Returning defaults.", _filePath);
                    return Validate(Defaults());
                }

                return Validate(settings);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex,
                    "[VCPSettings] Malformed JSON in '{Path}'. Returning defaults.", _filePath);
                return Validate(Defaults());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[VCPSettings] Failed to read '{Path}'. Returning defaults.", _filePath);
                return Validate(Defaults());
            }
        }

        /// <inheritdoc/>
        public void Save(VCPSettings settings)
        {
            if (settings is null) throw new ArgumentNullException(nameof(settings));

            try
            {
                string json = JsonSerializer.Serialize(settings, _jsonOptions);
                File.WriteAllText(_tempPath, json);

                string? backupPath = _filePath + ".bak";
                File.Replace(_tempPath, _filePath, backupPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[VCPSettings] File.Replace failed for '{Path}'. Attempting fallback write.", _filePath);

                try
                {
                    string json = JsonSerializer.Serialize(settings, _jsonOptions);
                    File.WriteAllText(_filePath, json);
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogCritical(fallbackEx,
                        "[VCPSettings] Fallback write also failed for '{Path}'. Settings not saved.", _filePath);
                    // Never throw from Save.
                }
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static VCPSettings Defaults() => new()
        {
            TradingMode              = VCPTradingMode.PaperTrade,
            Timeframe                = VCPTimeframe.FifteenMin,
            MaxConcurrentTrades      = 2,
            RiskAmountPerTrade       = 1_000m,
            UseAutoLotSizing         = false,
            FixedLotsPerTrade        = 1,
            Target1RR                = 1.5m,
            Target2RR                = 3.0m,
            Target1ExitPercent       = 50,
            ExitOnPatternFailure     = true,
            PatternFailureExitMode   = ExitMode.WaitForCandleClose,
            ExitOnReversalCandle     = true,
            ReversalCandleExitMode   = ExitMode.WaitForCandleClose,
            EnableEndOfDaySquareOff  = true,
            SquareOffTime            = new TimeSpan(15, 10, 0),
            MinVCPQuality            = VCPQuality.B,
            Watchlist                = new List<string> { "NIFTY", "BANKNIFTY" },
            NiftyLotSize             = 65,
            BankNiftyLotSize         = 30,
        };

        private VCPSettings Validate(VCPSettings s)
        {
            if (s.NiftyLotSize <= 0)
            {
                _logger.LogWarning("[VCPSettings] NiftyLotSize={V} invalid — overriding to 65.", s.NiftyLotSize);
                s.NiftyLotSize = 65;
            }

            if (s.BankNiftyLotSize <= 0)
            {
                _logger.LogWarning("[VCPSettings] BankNiftyLotSize={V} invalid — overriding to 30.", s.BankNiftyLotSize);
                s.BankNiftyLotSize = 30;
            }

            if (s.MaxConcurrentTrades > 4)
            {
                _logger.LogWarning("[VCPSettings] MaxConcurrentTrades={V} > 4 — clamping to 4.", s.MaxConcurrentTrades);
                s.MaxConcurrentTrades = 4;
            }

            if (s.MaxConcurrentTrades < 1)
            {
                _logger.LogWarning("[VCPSettings] MaxConcurrentTrades={V} < 1 — clamping to 1.", s.MaxConcurrentTrades);
                s.MaxConcurrentTrades = 1;
            }

            if (s.RiskAmountPerTrade < 100m)
            {
                _logger.LogWarning("[VCPSettings] RiskAmountPerTrade={V} < 100 — clamping to 100.", s.RiskAmountPerTrade);
                s.RiskAmountPerTrade = 100m;
            }

            if (s.SquareOffTime == default)
            {
                _logger.LogWarning("[VCPSettings] SquareOffTime is default — setting to 15:10:00.");
                s.SquareOffTime = new TimeSpan(15, 10, 0);
            }

            if (s.Target1RR >= s.Target2RR)
            {
                _logger.LogWarning(
                    "[VCPSettings] Target1RR={T1} >= Target2RR={T2} — resetting to 1.5 / 3.0.",
                    s.Target1RR, s.Target2RR);
                s.Target1RR = 1.5m;
                s.Target2RR = 3.0m;
            }

            if (s.Watchlist is null || s.Watchlist.Count == 0)
            {
                _logger.LogWarning("[VCPSettings] Watchlist is null/empty — resetting to [NIFTY, BANKNIFTY].");
                s.Watchlist = new List<string> { "NIFTY", "BANKNIFTY" };
            }

            return s;
        }
    }
}
