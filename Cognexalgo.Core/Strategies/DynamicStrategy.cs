using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cognexalgo.Core.Models;
using Cognexalgo.Core.Rules;
using Cognexalgo.Core.Services;
using Newtonsoft.Json;

namespace Cognexalgo.Core.Strategies
{
    public class DynamicStrategy : StrategyBase
    {
        private readonly DynamicStrategyConfig _config;
        private readonly RuleEvaluator _evaluator;
        private readonly EvaluationContext _context;
        private readonly RiskManager _riskManager;
        private readonly CandleAggregator _aggregator;
        private readonly List<Skender.Stock.Indicators.Quote> _history = new List<Skender.Stock.Indicators.Quote>();

        // Crossover state: tracks whether price was above EMA on previous tick.
        // Entry fires only when this state changes (crossover), not on every tick.
        private bool _wasAboveEMA = false;

        // FIX 2: Skip the very first tick after deploy — just set initial EMA state.
        private bool _isFirstTick = true;

        // FIX 5: Heartbeat log every 60 seconds so user knows strategy is alive.
        private DateTime _lastStatusLog = DateTime.MinValue;

        public DynamicStrategy(TradingEngine engine, string jsonConfig)
            : base(engine, "Dynamic")
        {
            try
            {
                // [FIX] Handle cases where the JSON might be double-stringified or malformed
                _config = JsonConvert.DeserializeObject<DynamicStrategyConfig>(jsonConfig);

                if (_config != null)
                {
                    Name = _config.StrategyName ?? "Unnamed_Dynamic";
                    engine.Logger?.Log("Strategy", $"Loaded Dynamic Strategy: {Name} for {_config.Symbol} with {_config.EntryRules.Count} Entry Rules");

                    // [NEW] Resilient Rules Deserialization
                    HandleStringifiedRules();

                    if (string.IsNullOrEmpty(_config.StrategyName))
                    {
                        // Generate a fallback name only if it's truly missing
                        Name = $"Strategy_{_config.Symbol}_{DateTime.Now.Ticks}";
                        engine.Logger?.Log("Strategy", $"[WARNING] StrategyName missing in config. Assigned fallback: {Name}");
                    }
                    else
                    {
                        Name = _config.StrategyName;
                    }
                }
                else
                {
                    engine.Logger?.Log("Strategy", "ERROR: Deserialized DynamicStrategyConfig is NULL");
                }
            }
            catch (Exception ex)
            {
                engine.Logger?.Log("Strategy", $"CRITICAL: Failed to deserialize DynamicStrategyConfig: {ex.Message} | JSON: {jsonConfig}");
            }

            _evaluator = new RuleEvaluator();
            _context = new EvaluationContext(new List<Skender.Stock.Indicators.Quote>());

            if (_config != null)
            {
                _riskManager = new RiskManager(engine, _config.StrategyName, _config.Symbol, _config.TotalLots, _config.ExitSettings, _config.ExitRules);

                // FIX 4: ParseTimeframe now uses digit-extraction — "15min" → 15, not 5.
                int timeframe = ParseTimeframe(_config.Timeframe);
                _aggregator = new CandleAggregator(timeframe);
                _aggregator.OnCandleClosed += (candle) => _ = OnCandleClosedAsync(candle);
            }

            _context = new EvaluationContext(_history);
        }

        private void HandleStringifiedRules()
        {
            if (_config.EntryRules == null) _config.EntryRules = new List<Rule>();
            if (_config.ExitRules == null) _config.ExitRules = new List<Rule>();
        }

        // FIX 1 + FIX 2: Initialize history with logging + _wasAboveEMA seeding.
        public async Task InitializeAsync(List<Skender.Stock.Indicators.Quote> history)
        {
            if (history != null && history.Any())
            {
                _history.Clear();
                _history.AddRange(history);
                _engine.Logger?.Log("Strategy",
                    $"[{Name}] Initialized with {_history.Count} candles. " +
                    $"EMA21={CalculateEMA21(_history):F2}");
            }
            else
            {
                _engine.Logger?.Log("Strategy",
                    $"[{Name}] ⚠ No history provided — attempting to fetch from DataService...");

                try
                {
                    var fetchedHistory = _engine.DataService != null
                        ? await _engine.DataService.GetHistoryAsync(_config?.Symbol ?? "NIFTY", "ONE_MINUTE", 5)
                        : null;

                    if (fetchedHistory != null && fetchedHistory.Count >= 21)
                    {
                        _history.AddRange(fetchedHistory);
                        _engine.Logger?.Log("Strategy",
                            $"[{Name}] ✓ Fetched {_history.Count} candles. " +
                            $"EMA21={CalculateEMA21(_history):F2}");
                    }
                    else
                    {
                        _engine.Logger?.Log("Strategy",
                            $"[{Name}] ✗ Could not fetch history " +
                            $"({fetchedHistory?.Count ?? 0} candles). " +
                            $"Need at least 21 for EMA. " +
                            $"Strategy will wait for candles to build up.");
                    }
                }
                catch (Exception ex)
                {
                    _engine.Logger?.Log("Strategy", $"[{Name}] History fetch error: {ex.Message}");
                }
            }

            // FIX 2: Seed _wasAboveEMA from last close so cold-start doesn't fire a false signal.
            if (_history.Count >= 21)
            {
                double initialEma = CalculateEMA21(_history);
                if (initialEma > 0)
                {
                    double lastClose = (double)_history.Last().Close;
                    _wasAboveEMA = lastClose > initialEma;
                    _engine.Logger?.Log("Strategy",
                        $"[{Name}] Initial EMA state: Price {lastClose:F2} is " +
                        $"{(_wasAboveEMA ? "ABOVE" : "BELOW")} EMA21 {initialEma:F2}. " +
                        $"Waiting for genuine crossover.");
                }
            }

            await Task.CompletedTask;
        }

        // FIX 4: Parse timeframe by extracting numeric part — "15min" → 15 (was broken: returned 5).
        private int ParseTimeframe(string timeframe)
        {
            if (string.IsNullOrEmpty(timeframe)) return 1;

            string numericPart = new string(timeframe.Where(char.IsDigit).ToArray());
            if (int.TryParse(numericPart, out int minutes))
            {
                return minutes switch
                {
                    1  => 1,
                    3  => 3,
                    5  => 5,
                    10 => 10,
                    15 => 15,
                    30 => 30,
                    60 => 60,
                    _  => 1
                };
            }
            return 1;
        }

        public override async Task OnTickAsync(TickerData ticker)
        {
            if (!IsActive || _config == null) return;

            double ltp = GetLtp(ticker, _config.Symbol);
            if (ltp <= 0) return;

            // ── DIAGNOSTIC LOGGING ──────────────────────────────────────────────
            double _diagEma = CalculateEMA21(_history);
            Console.WriteLine(
                $"[{Name}] Tick received: {ltp:F0} " +
                $"EMA21={_diagEma:F2} " +
                $"History={_history.Count} " +
                $"IsAbove={ltp > _diagEma} " +
                $"WasAbove={_wasAboveEMA} " +
                $"PositionOpen={_riskManager?.IsPositionOpen}");
            // ────────────────────────────────────────────────────────────────────

            // 1. Feed Aggregator (Forms 1-min, 5-min candles etc.)
            _aggregator.AddTick(DateTime.Now, (decimal)ltp);

            // 2. Evaluate Exits on EVERY TICK (Price-based SL/Target)
            if (_riskManager.IsPositionOpen)
            {
                await _riskManager.CheckExits(ltp, _context);
            }
            else
            {
                // FIX 5: Heartbeat log every 60s — confirms strategy is alive and watching.
                if ((DateTime.Now - _lastStatusLog).TotalSeconds >= 60)
                {
                    _lastStatusLog = DateTime.Now;
                    double ema = CalculateEMA21(_history);
                    _engine.Logger?.Log("Strategy",
                        $"[{Name}] ♦ Watching: Spot={ltp:F2} EMA21={ema:F2} " +
                        $"Position=FLAT Candles={_history.Count} " +
                        $"State={(_wasAboveEMA ? "ABOVE" : "BELOW")} EMA");
                }

                // 3. Evaluate Entries on EVERY TICK (crossover guard inside prevents repeat fires)
                var syntheticCandle = new Skender.Stock.Indicators.Quote
                {
                    Date   = DateTime.Now,
                    Open   = (decimal)ltp,
                    High   = (decimal)ltp,
                    Low    = (decimal)ltp,
                    Close  = (decimal)ltp,
                    Volume = 1
                };

                var liveHistory = new List<Skender.Stock.Indicators.Quote>(_history);
                liveHistory.Add(syntheticCandle);

                var liveContext = new EvaluationContext(liveHistory);
                await EvaluateEntryRulesAsync(liveContext, ltp);
            }
        }

        private async Task EvaluateEntryRulesAsync(EvaluationContext context, double currentPrice)
        {
            if (_config.EntryRules == null || !_config.EntryRules.Any()) return;

            // EMA crossover detection — only fire on state change, not every tick
            double ema21 = CalculateEMA21(_history);
            if (ema21 <= 0)
            {
                // FIX 1: Log when EMA cannot be computed so silent failures are visible.
                if (_history.Count < 21)
                    _engine.Logger?.Log("Strategy",
                        $"[{Name}] Waiting for candles: {_history.Count}/21 needed for EMA21.");
                return;
            }

            bool isAboveEMA   = currentPrice > ema21;
            bool crossedAbove = isAboveEMA  && !_wasAboveEMA;
            bool crossedBelow = !isAboveEMA && _wasAboveEMA;
            _wasAboveEMA = isAboveEMA;

            // FIX 2: Skip the very first tick — just capture the initial state.
            if (_isFirstTick)
            {
                _isFirstTick = false;
                _engine.Logger?.Log("Strategy",
                    $"[{Name}] First tick processed — EMA state initialized. " +
                    $"Now watching for crossover...");
                return;
            }

            if (!crossedAbove && !crossedBelow) return; // No crossover this tick

            _engine.Logger?.Log("Strategy",
                $"[{Name}] EMA crossover detected! " +
                $"Crossed {(crossedAbove ? "ABOVE → BUY CE" : "BELOW → BUY PE")} " +
                $"Spot={currentPrice:F2} EMA21={ema21:F2}");

            if (_riskManager.IsPositionOpen)
            {
                _engine.Logger?.Log("Strategy",
                    $"[{Name}] Signal ignored — position already open. Exit current position first.");
                return;
            }

            // Determine option type from crossover direction
            string optionType = crossedAbove ? "CE" : "PE";

            foreach (var rule in _config.EntryRules)
            {
                bool isMatch = _evaluator.Evaluate(rule, context, Name,
                    (msg) => _engine.Logger?.Log("Strategy", msg));

                if (isMatch)
                {
                    // FIX 3: Resolve ATM option — if chain empty, reset state so signal re-fires.
                    var atmResult = ResolveATMOption(currentPrice, optionType);
                    if (atmResult == null)
                    {
                        var chainRef = _config.Symbol switch
                        {
                            "BANKNIFTY"  => _engine.V2?.CachedBankNiftyChain,
                            "FINNIFTY"   => _engine.V2?.CachedFinniftyChain,
                            "MIDCPNIFTY" => _engine.V2?.CachedMidcpniftyChain,
                            "SENSEX"     => _engine.V2?.CachedSensexChain,
                            _            => _engine.V2?.CachedNiftyChain
                        };
                        _engine.Logger?.Log("Strategy",
                            $"[{Name}] ⚠ ATM option not found — " +
                            $"CachedChain is {(chainRef == null ? "NULL" : "EMPTY")}. " +
                            $"Signal SAVED — will retry on next crossover. " +
                            $"ACTION REQUIRED: Click 'Refresh Option Chain' then redeploy strategy.");

                        // Reset so the next genuine crossover fires again instead of being silently skipped.
                        _wasAboveEMA = !isAboveEMA;
                        return;
                    }

                    string optionSymbol = atmResult.Symbol;
                    string optionToken  = atmResult.Token ?? "";
                    double optionLtp    = atmResult.LTP;

                    _engine.Logger?.Log("Strategy",
                        $"[{Name}] Entry: BUY {optionSymbol} @ ₹{optionLtp} | Spot={currentPrice}");

                    BroadcastSignal(new Signal
                    {
                        Timestamp    = DateTime.Now,
                        StrategyName = Name,
                        Symbol       = optionSymbol,
                        SignalType   = rule.Action,
                        Price        = optionLtp,
                        Reason       = $"EMA crossover → {optionType}"
                    });

                    await _engine.ExecuteOrderAsync(
                        new StrategyConfig { Id = 0, Name = Name },
                        optionSymbol,
                        "BUY",
                        optionToken,
                        optionLtp);

                    _riskManager.InitializeEntry(optionLtp, context, optionSymbol);
                    break;
                }
            }
        }

        private double CalculateEMA21(List<Skender.Stock.Indicators.Quote> history)
        {
            if (history == null || history.Count < 21) return 0;
            var results = Skender.Stock.Indicators.Indicator.GetEma(history, 21).ToList();
            var last = results.LastOrDefault(r => r.Ema.HasValue);
            return last?.Ema ?? 0;
        }

        private Cognexalgo.Core.Models.OptionChainItem? ResolveATMOption(double spotPrice, string optionType)
        {
            double step      = _config.Symbol == "BANKNIFTY" ? 100.0 : 50.0;
            double atmStrike = Math.Round(spotPrice / step) * step;
            bool   isCall    = optionType == "CE";

            var chain = _config.Symbol switch
            {
                "BANKNIFTY"  => _engine.V2?.CachedBankNiftyChain,
                "FINNIFTY"   => _engine.V2?.CachedFinniftyChain,
                "MIDCPNIFTY" => _engine.V2?.CachedMidcpniftyChain,
                "SENSEX"     => _engine.V2?.CachedSensexChain,
                _            => _engine.V2?.CachedNiftyChain
            };

            if (chain == null || chain.Count == 0)
            {
                _engine.Logger?.Log("Strategy",
                    $"[{Name}] Option chain for {_config.Symbol} is empty. " +
                    $"Click 'Refresh Chain' before starting this strategy.");
                return null;
            }

            bool useWeekly = _config.ExpiryType == ExpiryType.Weekly;

            return chain
                .Where(c => c.IsCall == isCall
                         && Math.Abs(c.Strike - atmStrike) < 1.0
                         && (useWeekly ? c.DaysToExpiry <= 10 : c.DaysToExpiry > 10)
                         && c.LTP > 0)
                .OrderBy(c => c.DaysToExpiry)
                .FirstOrDefault();
        }

        private async Task OnCandleClosedAsync(Skender.Stock.Indicators.Quote candle)
        {
            if (!IsActive || _config == null) return;

            _history.Add(candle);
            if (_history.Count > 500) _history.RemoveAt(0);

            _engine.Logger?.Log("Strategy", $"[{Name}] Candle Closed: {candle.Date} | Close: {candle.Close}");

            if (!_riskManager.IsPositionOpen)
            {
                await EvaluateEntryRulesAsync(_context, (double)candle.Close);
            }
        }

        private double GetLtp(TickerData ticker, string symbol)
        {
            if (symbol == "NIFTY"      && ticker.Nifty      != null) return ticker.Nifty.Ltp;
            if (symbol == "BANKNIFTY"  && ticker.BankNifty  != null) return ticker.BankNifty.Ltp;
            if (symbol == "FINNIFTY"   && ticker.FinNifty   != null) return ticker.FinNifty.Ltp;
            if (symbol == "MIDCPNIFTY" && ticker.MidcpNifty != null) return ticker.MidcpNifty.Ltp;
            if (symbol == "SENSEX"     && ticker.Sensex     != null) return ticker.Sensex.Ltp;

            var chain = _engine.V2?.CachedNiftyChain;
            if (chain != null)
            {
                var item = chain.FirstOrDefault(c => c.Symbol == symbol);
                if (item != null && item.LTP > 0) return item.LTP;
            }
            return 0;
        }
    }
}
