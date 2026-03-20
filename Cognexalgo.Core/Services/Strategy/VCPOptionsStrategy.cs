using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Cognexalgo.Core.Application.Interfaces;
using Cognexalgo.Core.Domain.Patterns;
using Cognexalgo.Core.Models;

namespace Cognexalgo.Core.Services.Strategy
{
    /// <summary>
    /// Orchestrates the full VCP trading lifecycle: pattern scanning, signal generation,
    /// order placement, position management, and exit handling.
    /// Thread-safe — all shared state is protected by <see cref="_sem"/>.
    /// </summary>
    public sealed class VCPOptionsStrategy : IVCPStrategy
    {
        // ── Dependencies ──────────────────────────────────────────────────────
        private readonly IVCPDetector             _detector;
        private readonly IVCPRiskManager          _riskManager;
        private readonly IVCPSettingsService      _settingsService;
        private readonly IMarketDataService       _marketData;
        private readonly IOrderManagementService  _oms;
        private readonly IBrokerFactory           _brokerFactory;
        private readonly ILogger<VCPOptionsStrategy> _logger;

        // ── Shared state ──────────────────────────────────────────────────────
        private readonly Dictionary<string, VCPPattern> _activePatterns  = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Guid, VCPSignal>    _openSignals     = new();
        private readonly Dictionary<Guid, int>          _remainingLots   = new();
        private readonly HashSet<Guid>                  _t1HitFlags      = new();
        private readonly Dictionary<Guid, decimal>      _effectiveSL     = new();
        private readonly Dictionary<string, Candle>     _prevCandle      = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<Guid>                  _pendingPFExit   = new();  // pattern failure, WaitForCandleClose
        private readonly HashSet<Guid>                  _pendingRevExit  = new();  // reversal candle, WaitForCandleClose
        private readonly SemaphoreSlim                  _sem             = new(1, 1);

        private VCPSettings               _settings;
        private CancellationTokenSource?  _cts;
        private Timer?                    _eodTimer;

        public bool IsRunning { get; private set; }

        public event EventHandler<VCPSignal>?      OnSignalGenerated;
        public event EventHandler<VCPTradeResult>? OnTradeCompleted;

        // ── Constructor ───────────────────────────────────────────────────────

        public VCPOptionsStrategy(
            IVCPDetector              detector,
            IVCPRiskManager           riskManager,
            IVCPSettingsService       settingsService,
            IMarketDataService        marketData,
            IOrderManagementService   oms,
            IBrokerFactory            brokerFactory,
            ILogger<VCPOptionsStrategy> logger)
        {
            _detector        = detector;
            _riskManager     = riskManager;
            _settingsService = settingsService;
            _marketData      = marketData;
            _oms             = oms;
            _brokerFactory   = brokerFactory;
            _logger          = logger;

            // Load settings eagerly so the strategy is usable without StartAsync
            _settings = SafeLoadSettings();
        }

        // ── IVCPStrategy ──────────────────────────────────────────────────────

        /// <inheritdoc/>
        public async Task StartAsync(CancellationToken ct)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            try   { _settings = _settingsService.Load(); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[VCPStrategy] Settings reload failed on Start; continuing with last known settings.");
            }

            var resolvedMode = _brokerFactory.GetActiveMode(_settings.TradingMode);
            _logger.LogInformation(
                "[VCPStrategy] Starting. Mode={Mode}, Timeframe={TF}, Watchlist={Count} symbols.",
                resolvedMode, _settings.Timeframe, _settings.Watchlist.Count);

            // Subscribe to candle feeds for every symbol on the watchlist
            foreach (var symbol in _settings.Watchlist)
            {
                if (_settings.Timeframe is VCPTimeframe.Daily or VCPTimeframe.Both)
                    await _marketData.SubscribeCandlesAsync(symbol, "Daily", _cts.Token);
                if (_settings.Timeframe is VCPTimeframe.FifteenMin or VCPTimeframe.Both)
                    await _marketData.SubscribeCandlesAsync(symbol, "15min", _cts.Token);
            }

            _marketData.OnCandleFormed += candle => _ = OnNewCandle(candle);

            if (_settings.EnableEndOfDaySquareOff)
                SetupEodTimer();

            IsRunning = true;
            _logger.LogInformation("[VCPStrategy] Started. Mode: {Mode}, Timeframe: {Timeframe}",
                resolvedMode, _settings.Timeframe);
        }

        /// <inheritdoc/>
        public async Task StopAsync()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _eodTimer?.Dispose();
            _eodTimer = null;
            IsRunning = false;
            _logger.LogInformation("[VCPStrategy] Stopped.");
            await Task.CompletedTask;
        }

        // ── Core candle handler ───────────────────────────────────────────────

        /// <summary>
        /// Processes a newly closed candle: manages open positions and scans for new patterns.
        /// All exceptions are caught so a single bad candle never crashes the engine.
        /// </summary>
        public async Task OnNewCandle(Candle candle)
        {
            try
            {
                // Snapshot signals to check and previous candle — both under the lock
                List<VCPSignal> signalsToCheck;
                Candle?         prev;

                await _sem.WaitAsync();
                try
                {
                    signalsToCheck = _openSignals.Values
                        .Where(s => s.Pattern?.Symbol?.Equals(
                                candle.Symbol, StringComparison.OrdinalIgnoreCase) == true)
                        .ToList();

                    _prevCandle.TryGetValue(candle.Symbol, out prev);
                }
                finally { _sem.Release(); }

                // ── PART A: manage open positions ────────────────────────────
                foreach (var signal in signalsToCheck)
                    await CheckSignal(signal, candle, prev);

                // Update previous candle after processing
                await _sem.WaitAsync();
                try { _prevCandle[candle.Symbol] = candle; }
                finally { _sem.Release(); }

                // ── PART B: scan for new patterns ────────────────────────────
                int openCount;
                await _sem.WaitAsync();
                try { openCount = _openSignals.Count; }
                finally { _sem.Release(); }

                if (!_riskManager.CanOpenTrade(openCount, _settings.MaxConcurrentTrades))
                    return;

                List<Candle> history;
                try
                {
                    history = await _marketData.GetCandlesAsync(
                        candle.Symbol, candle.Timeframe, 65);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[VCPStrategy] GetCandlesAsync failed for {Symbol}. Skipping scan.", candle.Symbol);
                    return;
                }

                VCPPattern? pattern;
                try
                {
                    pattern = _detector.Detect(history, candle.Symbol, candle.Timeframe);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[VCPStrategy] Detect() threw for {Symbol}. Skipping.", candle.Symbol);
                    return;
                }

                if (pattern is null || !pattern.IsValid)
                    return;

                // Enum: A=0 (best), B=1, C=2 — reject patterns numerically ABOVE the threshold
                if (pattern.Quality > _settings.MinVCPQuality)
                    return;

                // Deduplicate — same pivot level on the same symbol is the same pattern
                bool isDuplicate;
                await _sem.WaitAsync();
                try
                {
                    isDuplicate = _activePatterns.TryGetValue(candle.Symbol, out var existing)
                               && existing.PivotLevel == pattern.PivotLevel;
                    if (!isDuplicate)
                        _activePatterns[candle.Symbol] = pattern;
                }
                finally { _sem.Release(); }

                if (isDuplicate)
                    return;

                if (_detector.IsBreakingOut(pattern, candle))
                    await CreateAndExecuteSignal(pattern, candle);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[VCPStrategy] Unhandled exception in OnNewCandle for {Symbol}", candle.Symbol);
            }
        }

        // ── Position management ───────────────────────────────────────────────

        private async Task CheckSignal(VCPSignal signal, Candle candle, Candle? prev)
        {
            // Read all relevant state under the lock (one acquisition)
            decimal effectiveSL;
            bool    t1Hit, pendingPF, pendingRev, stillOpen;

            await _sem.WaitAsync();
            try
            {
                stillOpen  = _openSignals.ContainsKey(signal.Id);
                effectiveSL = _effectiveSL.TryGetValue(signal.Id, out var eSL) ? eSL : signal.StopLoss;
                t1Hit      = _t1HitFlags.Contains(signal.Id);
                pendingPF  = _pendingPFExit.Contains(signal.Id);
                pendingRev = _pendingRevExit.Contains(signal.Id);
            }
            finally { _sem.Release(); }

            if (!stillOpen) return;

            // 1. Process deferred WaitForCandleClose exits (flag set on prior candle)
            if (pendingPF)
            {
                await ExitTrade(signal, candle, ExitTrigger.PatternFailure);
                return;
            }
            if (pendingRev)
            {
                await ExitTrade(signal, candle, ExitTrigger.ManualExit);
                return;
            }

            // 2. Stop-loss
            if (candle.Close <= effectiveSL)
            {
                await ExitTrade(signal, candle, ExitTrigger.StopLossHit);
                return;
            }

            // 3. Target 2 — full exit
            if (candle.Close >= signal.Target2)
            {
                await ExitTrade(signal, candle, ExitTrigger.Target2Hit);
                return;
            }

            // 4. Target 1 — partial exit (only once)
            if (!t1Hit && candle.Close >= signal.Target1)
            {
                await PartialExit(signal, candle);
                return;
            }

            // 5. Pattern failure check
            if (_settings.ExitOnPatternFailure
                && _activePatterns.TryGetValue(signal.Pattern.Symbol, out var activePat)
                && _detector.IsPatternFailed(activePat, candle))
            {
                if (_settings.PatternFailureExitMode == ExitMode.Immediate)
                {
                    await ExitTrade(signal, candle, ExitTrigger.PatternFailure);
                }
                else
                {
                    await _sem.WaitAsync();
                    try { _pendingPFExit.Add(signal.Id); }
                    finally { _sem.Release(); }
                }
                return;
            }

            // 6. Reversal candle check
            if (_settings.ExitOnReversalCandle && prev is not null
                && _detector.IsReversalCandle(candle, prev))
            {
                if (_settings.ReversalCandleExitMode == ExitMode.Immediate)
                {
                    await ExitTrade(signal, candle, ExitTrigger.ManualExit);
                }
                else
                {
                    await _sem.WaitAsync();
                    try { _pendingRevExit.Add(signal.Id); }
                    finally { _sem.Release(); }
                }
            }
        }

        private async Task CreateAndExecuteSignal(VCPPattern pattern, Candle candle)
        {
            decimal entry       = candle.Close;
            decimal sl          = pattern.TightLow;
            decimal riskPerUnit = entry - sl;
            decimal t1          = entry + riskPerUnit * _settings.Target1RR;
            decimal t2          = entry + riskPerUnit * _settings.Target2RR;
            decimal rrr         = riskPerUnit > 0 ? (t1 - entry) / riskPerUnit : 0m;

            int lotSize = GetLotSize(pattern.Symbol);
            int lots    = _settings.UseAutoLotSizing
                ? _riskManager.CalculateLots(entry, sl, _settings.RiskAmountPerTrade, lotSize)
                : _settings.FixedLotsPerTrade;

            var signal = new VCPSignal
            {
                Pattern          = pattern,
                EntryPrice       = entry,
                StopLoss         = sl,
                Target1          = t1,
                Target2          = t2,
                RiskRewardRatio  = rrr,
                SignalTime       = candle.Timestamp,
                IsActive         = true,
                SuggestedStrike  = $"{GetNearestStrike(entry, pattern.Symbol)} CE",
                SuggestedExpiry  = GetCurrentWeekExpiry(),
            };

            var order = new VCPOrder
            {
                SignalId         = signal.Id,
                Symbol           = pattern.Symbol,
                TradingSymbol    = signal.SuggestedStrike,
                TransactionType  = "BUY",
                Quantity         = lots * lotSize,
                Price            = entry,
                OrderType        = "MARKET",
                Mode             = _settings.TradingMode,
            };

            string? orderId;
            try
            {
                orderId = await _oms.PlaceOrderAsync(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[VCPStrategy] Entry order placement failed for {Symbol}. Signal discarded.",
                    pattern.Symbol);
                return;
            }

            if (orderId is null)
            {
                _logger.LogWarning(
                    "[VCPStrategy] Broker rejected entry order for {Symbol}. Signal discarded.",
                    pattern.Symbol);
                return;
            }

            await _sem.WaitAsync();
            try
            {
                _openSignals[signal.Id]  = signal;
                _remainingLots[signal.Id] = lots;
                _effectiveSL[signal.Id]   = sl;
            }
            finally { _sem.Release(); }

            OnSignalGenerated?.Invoke(this, signal);
            _logger.LogInformation("[VCPStrategy] New signal → {Summary}", signal.SignalSummary);
        }

        private async Task PartialExit(VCPSignal signal, Candle candle)
        {
            int currentLots;
            await _sem.WaitAsync();
            try
            {
                if (!_openSignals.ContainsKey(signal.Id)) return;
                _remainingLots.TryGetValue(signal.Id, out currentLots);
            }
            finally { _sem.Release(); }

            if (currentLots <= 0) return;

            int lotSize  = GetLotSize(signal.Pattern.Symbol);
            int exitLots = Math.Max(1, currentLots / 2);
            int exitQty  = exitLots * lotSize;

            var order = new VCPOrder
            {
                SignalId        = signal.Id,
                Symbol          = signal.Pattern.Symbol,
                TradingSymbol   = signal.SuggestedStrike,
                TransactionType = "SELL",
                Quantity        = exitQty,
                Price           = candle.Close,
                OrderType       = "MARKET",
                Mode            = _settings.TradingMode,
            };

            try
            {
                await _oms.PlaceOrderAsync(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[VCPStrategy] T1 partial exit order failed for signal {Id}.", signal.Id);
                return;
            }

            int remainingLots = currentLots - exitLots;

            await _sem.WaitAsync();
            try
            {
                _t1HitFlags.Add(signal.Id);
                _effectiveSL[signal.Id] = signal.EntryPrice;  // move SL to breakeven

                if (remainingLots <= 0)
                {
                    _openSignals.Remove(signal.Id);
                    _remainingLots.Remove(signal.Id);
                    _effectiveSL.Remove(signal.Id);
                    _activePatterns.Remove(signal.Pattern.Symbol);
                    _t1HitFlags.Remove(signal.Id);
                }
                else
                {
                    _remainingLots[signal.Id] = remainingLots;
                }
            }
            finally { _sem.Release(); }

            if (remainingLots <= 0)
            {
                // Treated as full exit at T1 (1-lot or similar indivisible position)
                var result = new VCPTradeResult
                {
                    SignalId     = signal.Id,
                    Symbol       = signal.Pattern.Symbol,
                    EntryPrice   = signal.EntryPrice,
                    ExitPrice    = candle.Close,
                    Quantity     = exitQty,
                    ExitTrigger  = ExitTrigger.Target1Hit,
                    EntryTime    = signal.SignalTime,
                    ExitTime     = candle.Timestamp,
                };
                OnTradeCompleted?.Invoke(this, result);
            }

            _logger.LogInformation(
                "[VCPStrategy] T1 partial exit: {Symbol} | exitLots={EL} remaining={RL} SL→breakeven={BE}",
                signal.Pattern.Symbol, exitLots, remainingLots, signal.EntryPrice);
        }

        private async Task ExitTrade(VCPSignal signal, Candle candle, ExitTrigger trigger)
        {
            int exitQty;
            await _sem.WaitAsync();
            try
            {
                if (!_openSignals.ContainsKey(signal.Id)) return;
                int lots = _remainingLots.TryGetValue(signal.Id, out var rl) ? rl : 1;
                exitQty  = lots * GetLotSize(signal.Pattern.Symbol);
            }
            finally { _sem.Release(); }

            var order = new VCPOrder
            {
                SignalId        = signal.Id,
                Symbol          = signal.Pattern.Symbol,
                TradingSymbol   = signal.SuggestedStrike,
                TransactionType = "SELL",
                Quantity        = exitQty,
                Price           = candle.Close,
                OrderType       = "MARKET",
                Mode            = _settings.TradingMode,
            };

            try
            {
                await _oms.PlaceOrderAsync(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[VCPStrategy] Exit order failed for signal {Id}. Position may be unhedged!", signal.Id);
                return;
            }

            await _sem.WaitAsync();
            try
            {
                _openSignals.Remove(signal.Id);
                _remainingLots.Remove(signal.Id);
                _effectiveSL.Remove(signal.Id);
                _t1HitFlags.Remove(signal.Id);
                _pendingPFExit.Remove(signal.Id);
                _pendingRevExit.Remove(signal.Id);
                _activePatterns.Remove(signal.Pattern.Symbol);
            }
            finally { _sem.Release(); }

            var result = new VCPTradeResult
            {
                SignalId    = signal.Id,
                Symbol      = signal.Pattern.Symbol,
                EntryPrice  = signal.EntryPrice,
                ExitPrice   = candle.Close,
                Quantity    = exitQty,
                ExitTrigger = trigger,
                EntryTime   = signal.SignalTime,
                ExitTime    = candle.Timestamp,
            };

            OnTradeCompleted?.Invoke(this, result);
            _logger.LogInformation(
                "[VCPStrategy] Trade closed: {Symbol} | trigger={Trigger} | PnL={PnL:N2}",
                signal.Pattern.Symbol, trigger, result.PnL);
        }

        private async Task EndOfDaySquareOff()
        {
            if (!IsRunning)
            {
                _logger.LogInformation("[VCPStrategy] EOD timer fired but strategy is stopped. Skipping.");
                return;
            }

            var now         = DateTime.Now;
            var marketClose = now.Date.AddHours(15).AddMinutes(30);
            if (now > marketClose)
            {
                _logger.LogWarning(
                    "[VCPStrategy] EOD timer fired after market close ({Time}). Skipping.",
                    now.TimeOfDay);
                return;
            }

            List<(VCPSignal signal, Candle? lastCandle)> toClose;
            await _sem.WaitAsync();
            try
            {
                toClose = _openSignals.Values
                    .Select(s => (s, _prevCandle.TryGetValue(s.Pattern.Symbol, out var c) ? c : (Candle?)null))
                    .ToList();
            }
            finally { _sem.Release(); }

            int closed = 0;
            foreach (var (signal, lastCandle) in toClose)
            {
                if (lastCandle is null)
                {
                    _logger.LogWarning(
                        "[VCPStrategy] EOD: no last candle for {Symbol}. Skipping that position.",
                        signal.Pattern.Symbol);
                    continue;
                }

                await ExitTrade(signal, lastCandle, ExitTrigger.EndOfDaySquareOff);
                closed++;
            }

            _logger.LogInformation("[VCPStrategy] EOD square-off complete. {Count} trade(s) closed.", closed);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private int GetLotSize(string symbol) =>
            symbol.ToUpperInvariant() switch
            {
                "BANKNIFTY" => _settings.BankNiftyLotSize,
                "NIFTY"     => _settings.NiftyLotSize,
                _           => 1,
            };

        private static int GetNearestStrike(decimal price, string symbol)
        {
            int step = symbol.ToUpperInvariant() == "BANKNIFTY" ? 100 : 50;
            return (int)(Math.Round(price / step) * step);
        }

        private static string GetCurrentWeekExpiry()
        {
            var today = DateTime.Today;
            int daysUntilThursday = ((int)DayOfWeek.Thursday - (int)today.DayOfWeek + 7) % 7;
            return today.AddDays(daysUntilThursday).ToString("dd-MMM-yyyy");
        }

        private void SetupEodTimer()
        {
            var now       = DateTime.Now;
            var squareOff = now.Date + _settings.SquareOffTime;
            if (squareOff <= now)
                squareOff = squareOff.AddDays(1);

            var delay = squareOff - now;
            _eodTimer = new Timer(_ => _ = EndOfDaySquareOff(), null, delay, Timeout.InfiniteTimeSpan);
        }

        private VCPSettings SafeLoadSettings()
        {
            try   { return _settingsService.Load(); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[VCPStrategy] Failed to load settings; using defaults.");
                return new VCPSettings();
            }
        }
    }
}
