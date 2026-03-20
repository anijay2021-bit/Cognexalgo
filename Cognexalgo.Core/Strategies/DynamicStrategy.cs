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
        private readonly DynamicStrategyConfig? _config;
        private readonly RuleEvaluator _evaluator;
        private readonly EvaluationContext _context;
        private readonly RiskManager _riskManager;
        private readonly CandleAggregator _aggregator;
        private readonly List<Skender.Stock.Indicators.Quote> _history = new List<Skender.Stock.Indicators.Quote>();

        // Crossover state: tracks whether price was above EMA on previous tick.
        private bool _wasAboveEMA = false;

        // Skip very first tick after deploy — just set initial EMA state.
        private bool _isFirstTick = true;

        // Heartbeat log every 60 seconds.
        private DateTime _lastStatusLog = DateTime.MinValue;

        // FIX 2: Cache the last TickerData so option LTP can be looked up at entry/exit time.
        private TickerData? _lastTickerData;

        // FIX 4: Track the open option token/symbol so exit path uses option LTP, not spot.
        private string _currentOptionToken  = string.Empty;
        private string _currentOptionSymbol = string.Empty;

        public DynamicStrategy(TradingEngine engine, string jsonConfig)
            : base(engine, "Dynamic")
        {
            try
            {
                _config = JsonConvert.DeserializeObject<DynamicStrategyConfig>(jsonConfig);

                if (_config != null)
                {
                    Name = _config.StrategyName ?? "Unnamed_Dynamic";
                    engine.Logger?.Log("Strategy", $"Loaded Dynamic Strategy: {Name} for {_config.Symbol} with {_config.EntryRules.Count} Entry Rules");

                    HandleStringifiedRules();

                    if (string.IsNullOrEmpty(_config.StrategyName))
                    {
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

                int timeframe = ParseTimeframe(_config.Timeframe);
                _aggregator = new CandleAggregator(timeframe);
                _aggregator.OnCandleClosed += (candle) => _ = OnCandleClosedAsync(candle);
            }

            _context = new EvaluationContext(_history);
        }

        private void HandleStringifiedRules()
        {
            if (_config is null) return;
            if (_config.EntryRules == null) _config.EntryRules = new List<Rule>();
            if (_config.ExitRules == null) _config.ExitRules = new List<Rule>();
        }

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

            // Seed _wasAboveEMA from last close so cold-start doesn't fire a false signal.
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

        // FIX 4: ParseTimeframe — digit extraction prevents "15min"→5 bug.
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

            // FIX 2: Store latest ticker so option LTP lookups can use it at entry/exit.
            _lastTickerData = ticker;

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

            // 1. Feed Aggregator
            _aggregator.AddTick(DateTime.Now, (decimal)ltp);

            // 2. Exits or Entries
            if (_riskManager.IsPositionOpen)
            {
                // FIX 4: Use live option LTP for exit checks, not underlying spot.
                double optLtp = GetOptionLtp();
                await _riskManager.CheckExits(optLtp > 0 ? optLtp : ltp, _context);
            }
            else
            {
                // Heartbeat log every 60s.
                if ((DateTime.Now - _lastStatusLog).TotalSeconds >= 60)
                {
                    _lastStatusLog = DateTime.Now;
                    double ema = CalculateEMA21(_history);
                    _engine.Logger?.Log("Strategy",
                        $"[{Name}] ♦ Watching: Spot={ltp:F2} EMA21={ema:F2} " +
                        $"Position=FLAT Candles={_history.Count} " +
                        $"State={(_wasAboveEMA ? "ABOVE" : "BELOW")} EMA");
                }

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

        // FIX 4: Resolve current open option's live LTP for exit monitoring.
        private double GetOptionLtp()
        {
            if (string.IsNullOrEmpty(_currentOptionToken)) return 0;

            // 1. SmartStream live feed
            double optLtp = _engine.SmartStream?.GetLastLtp(_currentOptionToken) ?? 0;
            if (optLtp > 0) return optLtp;

            // 2. Last ticker data Options dictionary
            if (_lastTickerData != null &&
                _lastTickerData.Options.TryGetValue(_currentOptionToken, out var info) &&
                info.Ltp > 0)
                return info.Ltp;

            // 3. Cached chain (kept fresh by MainViewModel.OnTick)
            if (!string.IsNullOrEmpty(_currentOptionSymbol))
            {
                var chain = _engine.V2?.CachedNiftyChain;
                var item = chain?.FirstOrDefault(c => c.Symbol == _currentOptionSymbol);
                if (item != null && item.LTP > 0) return item.LTP;
            }

            return 0;
        }

        private async Task EvaluateEntryRulesAsync(EvaluationContext context, double currentPrice)
        {
            if (_config is null || _config.EntryRules == null || !_config.EntryRules.Any()) return;

            double ema21 = CalculateEMA21(_history);
            if (ema21 <= 0)
            {
                if (_history.Count < 21)
                    _engine.Logger?.Log("Strategy",
                        $"[{Name}] Waiting for candles: {_history.Count}/21 needed for EMA21.");
                return;
            }

            bool isAboveEMA   = currentPrice > ema21;
            bool crossedAbove = isAboveEMA  && !_wasAboveEMA;
            bool crossedBelow = !isAboveEMA && _wasAboveEMA;
            _wasAboveEMA = isAboveEMA;

            // Skip very first tick — just capture initial state.
            if (_isFirstTick)
            {
                _isFirstTick = false;
                _engine.Logger?.Log("Strategy",
                    $"[{Name}] First tick processed — EMA state initialized. " +
                    $"Now watching for crossover...");
                return;
            }

            if (!crossedAbove && !crossedBelow) return;

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

            string optionType = crossedAbove ? "CE" : "PE";

            foreach (var rule in _config.EntryRules)
            {
                bool isMatch = _evaluator.Evaluate(rule, context, Name,
                    (msg) => _engine.Logger?.Log("Strategy", msg));

                if (isMatch)
                {
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
                        _wasAboveEMA = !isAboveEMA;
                        return;
                    }

                    string optionSymbol = atmResult.Symbol;
                    string optionToken  = atmResult.Token ?? "";

                    // FIX 1: Subscribe to SmartStream and get live LTP, not stale chain snapshot.
                    if (!string.IsNullOrEmpty(optionToken) &&
                        _engine.SmartStream?.IsConnected == true)
                    {
                        await _engine.SmartStream.SubscribeAsync(
                            new List<string> { optionToken }, "NFO");
                        // Brief wait for first tick to arrive on this token
                        await Task.Delay(500);
                    }

                    // 1. Try SmartStream live feed
                    double optionLtp = !string.IsNullOrEmpty(optionToken)
                        ? _engine.SmartStream?.GetLastLtp(optionToken) ?? 0
                        : 0;

                    // 2. Fallback: ticker data Options dictionary
                    if (optionLtp <= 0 && _lastTickerData != null &&
                        !string.IsNullOrEmpty(optionToken) &&
                        _lastTickerData.Options.TryGetValue(optionToken, out var tickInfo) &&
                        tickInfo.Ltp > 0)
                    {
                        optionLtp = tickInfo.Ltp;
                    }

                    // 3. Fallback: chain LTP (kept fresh by MainViewModel chain refresh)
                    if (optionLtp <= 0)
                    {
                        optionLtp = atmResult.LTP;
                        _engine.Logger?.Log("Strategy",
                            $"[{Name}] ⚠ Using chain LTP ₹{optionLtp:F2} for {optionSymbol} " +
                            $"— SmartStream has no live price yet. Actual fill may differ.");
                    }
                    else
                    {
                        _engine.Logger?.Log("Strategy",
                            $"[{Name}] ✓ Live LTP from SmartStream: ₹{optionLtp:F2} for {optionSymbol}");
                    }

                    // Store open position identifiers for exit LTP resolution.
                    _currentOptionToken  = optionToken;
                    _currentOptionSymbol = optionSymbol;

                    _engine.Logger?.Log("Strategy",
                        $"[{Name}] Entry: BUY {optionSymbol} @ ₹{optionLtp:F2} | Spot={currentPrice:F2}");

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
            if (_config is null) return null;
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
