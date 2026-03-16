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
                
                // Initialize Aggregator based on timeframe
                int timeframe = ParseTimeframe(_config.Timeframe);
                _aggregator = new CandleAggregator(timeframe);
                _aggregator.OnCandleClosed += (candle) => _ = OnCandleClosedAsync(candle);
            }
            
            _context = new EvaluationContext(_history);
        }

        private void HandleStringifiedRules()
        {
            // The ResilientRuleListConverter handles the property-level deserialization 
            // the constructor just ensure the Name is set and other basics.
            // But if we ever need to manually fix them, we can do it here.
            if (_config.EntryRules == null) _config.EntryRules = new List<Rule>();
            if (_config.ExitRules == null) _config.ExitRules = new List<Rule>();
        }

        public async Task InitializeAsync(List<Skender.Stock.Indicators.Quote> history)
        {
            if (history != null && history.Any())
            {
                _history.Clear();
                _history.AddRange(history);
                Console.WriteLine($"[Strategy] {Name} initialized with {_history.Count} historical candles.");
            }
            await Task.CompletedTask;
        }

        private int ParseTimeframe(string timeframe)
        {
            if (string.IsNullOrEmpty(timeframe)) return 1;
            if (timeframe.Contains("5")) return 5;
            if (timeframe.Contains("15")) return 15;
            if (timeframe.Contains("30")) return 30;
            if (timeframe.Contains("60")) return 60;
            return 1; // Default 1 min
        }

        public override async Task OnTickAsync(TickerData ticker)
        {
            if (!IsActive || _config == null) return;

            double ltp = GetLtp(ticker, _config.Symbol);
            if (ltp <= 0) return;

            // 1. Feed Aggregator (Forms 1-min, 5-min candles etc.)
            _aggregator.AddTick(DateTime.Now, (decimal)ltp);

            // 2. Evaluate Exits on EVERY TICK (Price-based SL/Target)
            if (_riskManager.IsPositionOpen)
            {
                await _riskManager.CheckExits(ltp, _context);
            }
            else
            {
                // 3. Evaluate Entries on EVERY TICK (crossover guard inside prevents repeat fires)
                // We construct a temporary context that includes the historically closed
                // candles PLUS the currently forming synthetic candle at this exact tick.
                var syntheticCandle = new Skender.Stock.Indicators.Quote 
                { 
                    Date = DateTime.Now, 
                    Open = (decimal)ltp, 
                    High = (decimal)ltp, 
                    Low = (decimal)ltp, 
                    Close = (decimal)ltp, 
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
            if (ema21 <= 0) return;

            bool isAboveEMA   = currentPrice > ema21;
            bool crossedAbove = isAboveEMA  && !_wasAboveEMA;
            bool crossedBelow = !isAboveEMA && _wasAboveEMA;
            _wasAboveEMA = isAboveEMA;

            if (!crossedAbove && !crossedBelow) return; // No crossover this tick

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
                    // Resolve ATM option from cached option chain
                    var atmResult = ResolveATMOption(currentPrice, optionType);
                    if (atmResult == null)
                    {
                        _engine.Logger?.Log("Strategy",
                            $"[{Name}] ATM option not found in chain. " +
                            $"Spot={currentPrice}, Type={optionType}. " +
                            $"Ensure option chain is loaded before strategy starts.");
                        return;
                    }

                    string optionSymbol = atmResult.Symbol;
                    string optionToken  = atmResult.Token ?? "";
                    double optionLtp    = atmResult.LTP;

                    _engine.Logger?.Log("Strategy",
                        $"[{Name}] Entry Signal: BUY {optionSymbol} @ ₹{optionLtp} | Spot={currentPrice}");

                    BroadcastSignal(new Signal
                    {
                        Timestamp    = DateTime.Now,
                        StrategyName = Name,
                        Symbol       = optionSymbol,
                        SignalType   = rule.Action,
                        Price        = optionLtp,
                        Reason       = $"EMA crossover → {optionType}"
                    });

                    // Execute order with option symbol + token + current LTP as fill price
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
            // Round to nearest 50 for Nifty, nearest 100 for BankNifty
            double step = _config.Symbol == "BANKNIFTY" ? 100.0 : 50.0;
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

            // Weekly = nearest expiry (DaysToExpiry <= 10); Monthly = further expiry
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

            // 1. Add closed candle to history for Indicator calculation
            _history.Add(candle);
            
            // Limit history size to 500 candles to save memory
            if (_history.Count > 500) _history.RemoveAt(0);

            _engine.Logger?.Log("Strategy", $"[{Name}] Candle Closed: {candle.Date} | Close: {candle.Close}");

            // 2. Evaluate Entry Rules on CANDLE CLOSE (Fallback/Standard)
            // If the position wasn't already opened during the live ticks of this candle,
            // check again on the confirmed close.
            if (!_riskManager.IsPositionOpen)
            {
                 // We can reuse the same tick evaluation logic with the standard context 
                 // since _history now includes the recently closed candle.
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

            // Option symbol — look up from cached chain
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
