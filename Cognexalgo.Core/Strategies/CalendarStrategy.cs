using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cognexalgo.Core.Models;
using Cognexalgo.Core.Services;

namespace Cognexalgo.Core.Strategies
{
    /// <summary>
    /// Calendar Spread Strategy with Hedge Support.
    ///
    /// Core Logic:
    ///   1. At FirstEntryTime — Buy next-month ATM straddle + Sell nearest-weekly ATM straddle.
    ///   2. Combined sell entry price (CE+PE) = SL for each individual sell leg.
    ///   3. Sell SL hit — Exit sell + Exit its hedge (same tick) — Flip to Buy with BuySL%.
    ///   4. Flipped buy SL hit — Exit buy — Flip back to Sell with new combined SL.
    ///   5. At WeeklyExpiryExitTime — Exit weekly legs + their hedges — Roll to next weekly.
    ///   6. Monthly expiry day — If buy strike == new weekly ATM — skip sell — hold buys.
    ///   7. MaxProfit / MaxLoss — Exit all legs + all hedges immediately.
    ///
    /// Hedge Logic (when EnableHedgeBuying = true):
    ///   - Triggered 1 day before weekly expiry AND 1 day before monthly expiry.
    ///   - Only buys hedge for legs where Action == "SELL" (not for flipped buy legs).
    ///   - CE hedge: Buy strike = Sell strike + (HedgeStrikeOffset × StrikeStep).
    ///   - PE hedge: Buy strike = Sell strike - (HedgeStrikeOffset × StrikeStep).
    ///   - Hedge exits IMMEDIATELY with its sell leg — no exceptions.
    ///   - HedgeBought flag reset on weekly roll so new hedges bought next cycle.
    /// </summary>
    public class CalendarStrategy : StrategyBase
    {
        private readonly CalendarStrategyConfig    _cfg;
        private          CalendarStrategyState     _state = new();
        private readonly CandleAggregator          _aggregator;
        private readonly List<Skender.Stock.Indicators.Quote> _candles = new();
        private readonly ICalendarStateRepository? _repo;

        private List<OptionChainItem>? OptionChain =>
            _cfg.Symbol switch
            {
                "BANKNIFTY"  => _engine.V2?.CachedBankNiftyChain,
                "FINNIFTY"   => _engine.V2?.CachedFinniftyChain,
                "MIDCPNIFTY" => _engine.V2?.CachedMidcpniftyChain,
                "SENSEX"     => _engine.V2?.CachedSensexChain,
                _            => _engine.V2?.CachedNiftyChain
            };

        public CalendarStrategyState State => _state;

        public CalendarStrategy(TradingEngine engine, CalendarStrategyConfig config,
                                ICalendarStateRepository? repo = null)
            : base(engine, "Calendar")
        {
            _cfg  = config;
            _repo = repo;
            Name  = config.Name;

            _aggregator = new CandleAggregator(_cfg.Timeframe);
            _aggregator.OnCandleClosed += candle =>
            {
                _candles.Add(candle);
                if (_candles.Count > 200) _candles.RemoveAt(0);
                _ = OnCandleClosedAsync(candle);
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        // PERSISTENCE HELPERS
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Replaces the internal state with a previously persisted snapshot.
        /// Call this BEFORE the strategy is registered with the engine.
        /// </summary>
        public void LoadState(CalendarStrategyState savedState)
        {
            _state = savedState;
            IsActive = true;
            _state.Log("Strategy resumed from saved state.");
        }

        /// <summary>Returns tokens for all currently-OPEN legs (used to re-subscribe after resume).</summary>
        public List<string> GetOpenLegTokens()
        {
            var tokens = new List<string>();
            foreach (var leg in new[] {
                _state.BuyCallLeg,  _state.BuyPutLeg,
                _state.SellCallLeg, _state.SellPutLeg,
                _state.HedgeCallLeg, _state.HedgePutLeg })
            {
                if (leg.Status == "OPEN" && !string.IsNullOrEmpty(leg.Token))
                    tokens.Add(leg.Token);
            }
            return tokens;
        }

        private void SaveState()
        {
            try { _repo?.Save(_cfg, _state); }
            catch (Exception ex) { _state.Log($"WARNING: State save failed: {ex.Message}"); }
        }

        private void RecordEvent(string eventType, string legDesc,
            double entryPrice = 0, double exitPrice = 0, double pnl = 0,
            bool wasHedged = false, double hedgeCost = 0)
        {
            double prev = _state.PerformanceLog.Count > 0
                ? _state.PerformanceLog[_state.PerformanceLog.Count - 1].CumulativePnL
                : 0;
            _state.PerformanceLog.Add(new CalendarPerformanceRecord
            {
                Date            = DateTime.Now,
                EventType       = eventType,
                LegDescription  = legDesc,
                EntryPrice      = entryPrice,
                ExitPrice       = exitPrice,
                PnL             = pnl,
                CumulativePnL   = prev + pnl,
                WasHedged       = wasHedged,
                HedgeCost       = hedgeCost
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // TICK ENTRY POINT
        // ─────────────────────────────────────────────────────────────────────

        public override async Task OnTickAsync(TickerData ticker)
        {
            if (!IsActive || _cfg == null) return;

            double spotLtp = GetSpotLtp(ticker, _cfg.Symbol);
            if (spotLtp <= 0) return;

            _aggregator.AddTick(DateTime.Now, (decimal)spotLtp);
            UpdateLegLTPs(ticker);

            var now   = DateTime.Now.TimeOfDay;
            var today = DateTime.Today;

            // ── Wait for first entry ──────────────────────────────────────────
            if (_state.Phase == CalendarPhase.WaitingFirstEntry)
            {
                if (now >= _cfg.FirstEntryTime && !_state.FirstEntryDone)
                    await ExecuteFirstEntryAsync(spotLtp, today);
                return;
            }

            // ── Update PnL and check max limits ───────────────────────────────
            UpdateUnrealizedPnL();
            if (await CheckMaxProfitLossAsync()) return;

            // ── Monthly expiry day — wait for exit time ───────────────────────
            if (_state.Phase == CalendarPhase.MonthlyExpiryDay)
            {
                if (now >= _cfg.WeeklyExpiryExitTime)
                    await ExitAllAndCompleteAsync("Monthly expiry");
                return;
            }

            // ── Hedge buying — 1 day before weekly expiry ─────────────────────
            if (_cfg.EnableHedgeBuying && !_state.HedgeBought)
            {
                bool oneDayBeforeWeekly  = today == _state.CurrentWeeklyExpiry.Date.AddDays(-1);
                bool oneDayBeforeMonthly = today == _state.MonthlyExpiry.Date.AddDays(-1);

                if (oneDayBeforeWeekly || oneDayBeforeMonthly)
                    await BuyHedgesAsync();
            }

            // ── Weekly expiry roll ────────────────────────────────────────────
            if (_state.Phase == CalendarPhase.Active
                && today == _state.CurrentWeeklyExpiry.Date
                && now >= _cfg.WeeklyExpiryExitTime)
            {
                await RollWeeklyLegsAsync(spotLtp, today);
                return;
            }

            // ── SL checks on LTP basis ────────────────────────────────────────
            if (_state.Phase == CalendarPhase.Active && !_cfg.EnableBuySLOnCandleClose)
                await CheckAllSLsAsync();
        }

        // ─────────────────────────────────────────────────────────────────────
        // CANDLE CLOSE
        // ─────────────────────────────────────────────────────────────────────

        private async Task OnCandleClosedAsync(Skender.Stock.Indicators.Quote candle)
        {
            if (_state.Phase != CalendarPhase.Active) return;
            if (_cfg.EnableBuySLOnCandleClose)
                await CheckAllSLsAsync();
        }

        // ─────────────────────────────────────────────────────────────────────
        // FIRST ENTRY
        // ─────────────────────────────────────────────────────────────────────

        private async Task ExecuteFirstEntryAsync(double spotPrice, DateTime today)
        {
            _state.FirstEntryDone = true;
            double atmStrike = Math.Round(spotPrice / _cfg.StrikeStep) * _cfg.StrikeStep;
            _state.ATMStrike = atmStrike;
            _state.Log($"First entry triggered. Spot={spotPrice}, ATM={atmStrike}");

            var monthlyExpiry = GetNextMonthlyExpiry(today);
            var weeklyExpiry  = GetNearestWeeklyExpiry(today);

            if (monthlyExpiry == default || weeklyExpiry == default)
            {
                _state.Log("ERROR: Could not resolve expiry dates. " +
                           "Ensure option chain is loaded before starting.");
                _state.FirstEntryDone = false;
                return;
            }

            _state.MonthlyExpiry        = monthlyExpiry;
            _state.CurrentWeeklyExpiry  = weeklyExpiry;
            _state.MonthlyExpiryIsToday = today == monthlyExpiry.Date;
            _state.HedgeBought          = false;

            _state.Log($"Monthly={monthlyExpiry:dd-MMM-yy}, Weekly={weeklyExpiry:dd-MMM-yy}");

            // ── BUY next-month ATM straddle ───────────────────────────────────
            var buyCall = ResolveOption(atmStrike, isCall: true,  isWeekly: false);
            var buyPut  = ResolveOption(atmStrike, isCall: false, isWeekly: false);

            if (buyCall == null || buyPut == null)
            {
                _state.Log("ERROR: Monthly ATM straddle not found in option chain.");
                _state.FirstEntryDone = false;
                return;
            }

            await PlaceLegAsync(_state.BuyCallLeg, buyCall, "BUY");
            await PlaceLegAsync(_state.BuyPutLeg,  buyPut,  "BUY");

            // ── SELL nearest-weekly ATM straddle (skip on monthly expiry day) ──
            if (!_state.MonthlyExpiryIsToday)
            {
                var sellCall = ResolveOption(atmStrike, isCall: true,  isWeekly: true);
                var sellPut  = ResolveOption(atmStrike, isCall: false, isWeekly: true);

                if (sellCall == null || sellPut == null)
                {
                    _state.Log("ERROR: Weekly ATM straddle not found in option chain.");
                    return;
                }

                await PlaceLegAsync(_state.SellCallLeg, sellCall, "SELL");
                await PlaceLegAsync(_state.SellPutLeg,  sellPut,  "SELL");

                _state.CombinedSellEntryPrice =
                    _state.SellCallLeg.EntryPrice + _state.SellPutLeg.EntryPrice;
                _state.SellCallLeg.SLPrice = _state.CombinedSellEntryPrice;
                _state.SellPutLeg.SLPrice  = _state.CombinedSellEntryPrice;

                _state.Log($"Sell CE={_state.SellCallLeg.EntryPrice:F2}, " +
                           $"PE={_state.SellPutLeg.EntryPrice:F2}, " +
                           $"CombinedSL={_state.CombinedSellEntryPrice:F2}");
            }

            _state.Phase = CalendarPhase.Active;
            _state.Log("Strategy ACTIVE.");
            RecordEvent("ENTRY",
                $"ATM={_state.ATMStrike} Monthly={_state.MonthlyExpiry:dd-MMM} Weekly={_state.CurrentWeeklyExpiry:dd-MMM}",
                entryPrice: _state.CombinedSellEntryPrice);
            SaveState();
        }

        // ─────────────────────────────────────────────────────────────────────
        // HEDGE BUYING
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Buys hedge positions for weekly SELL legs only.
        /// Called 1 day before weekly expiry and 1 day before monthly expiry.
        /// Skips hedge if the sell leg has already flipped to BUY.
        /// CE hedge = sell strike + (offset × strikeStep) strikes above.
        /// PE hedge = sell strike - (offset × strikeStep) strikes below.
        /// </summary>
        private async Task BuyHedgesAsync()
        {
            _state.Log($"Buying hedges (offset={_cfg.HedgeStrikeOffset} strikes).");
            bool anyHedgeBought = false;

            // ── Hedge for Sell Call ───────────────────────────────────────────
            if (_state.SellCallLeg.Status == "OPEN" &&
                _state.SellCallLeg.Action == "SELL" &&
                !_state.HedgeCallLeg.IsHedgeLeg)
            {
                double hedgeStrike = _state.SellCallLeg.Strike +
                    (_cfg.HedgeStrikeOffset * _cfg.StrikeStep);

                var hedgeCallItem = ResolveOptionByStrike(
                    hedgeStrike, isCall: true,
                    _state.SellCallLeg.IsWeeklyExpiry);

                if (hedgeCallItem != null)
                {
                    _state.HedgeCallLeg = new CalendarLeg { IsHedgeLeg = true };
                    await PlaceLegAsync(_state.HedgeCallLeg, hedgeCallItem, "BUY");
                    _state.Log($"Hedge CE bought: {hedgeCallItem.TradingSymbol} " +
                               $"Strike={hedgeStrike} @ ₹{hedgeCallItem.LTP:F2}");
                    anyHedgeBought = true;
                }
                else
                {
                    _state.Log($"WARNING: Hedge CE strike {hedgeStrike} not found in chain.");
                }
            }
            else if (_state.SellCallLeg.Action == "BUY")
            {
                _state.Log("Sell CE already flipped to BUY — skipping CE hedge.");
            }

            // ── Hedge for Sell Put ────────────────────────────────────────────
            if (_state.SellPutLeg.Status == "OPEN" &&
                _state.SellPutLeg.Action == "SELL" &&
                !_state.HedgePutLeg.IsHedgeLeg)
            {
                double hedgeStrike = _state.SellPutLeg.Strike -
                    (_cfg.HedgeStrikeOffset * _cfg.StrikeStep);

                var hedgePutItem = ResolveOptionByStrike(
                    hedgeStrike, isCall: false,
                    _state.SellPutLeg.IsWeeklyExpiry);

                if (hedgePutItem != null)
                {
                    _state.HedgePutLeg = new CalendarLeg { IsHedgeLeg = true };
                    await PlaceLegAsync(_state.HedgePutLeg, hedgePutItem, "BUY");
                    _state.Log($"Hedge PE bought: {hedgePutItem.TradingSymbol} " +
                               $"Strike={hedgeStrike} @ ₹{hedgePutItem.LTP:F2}");
                    anyHedgeBought = true;
                }
                else
                {
                    _state.Log($"WARNING: Hedge PE strike {hedgeStrike} not found in chain.");
                }
            }
            else if (_state.SellPutLeg.Action == "BUY")
            {
                _state.Log("Sell PE already flipped to BUY — skipping PE hedge.");
            }

            if (anyHedgeBought)
            {
                _state.HedgeBought = true;
                double callCost = _state.HedgeCallLeg.IsHedgeLeg
                    ? _state.HedgeCallLeg.EntryPrice * _cfg.TotalQty : 0;
                double putCost  = _state.HedgePutLeg.IsHedgeLeg
                    ? _state.HedgePutLeg.EntryPrice  * _cfg.TotalQty : 0;
                RecordEvent("HEDGE_BUY",
                    $"CE hedge @ {_state.HedgeCallLeg.TradingSymbol}  |  " +
                    $"PE hedge @ {_state.HedgePutLeg.TradingSymbol}",
                    pnl: -(callCost + putCost),
                    hedgeCost: callCost + putCost);
                SaveState();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // SL CHECKS
        // ─────────────────────────────────────────────────────────────────────

        private async Task CheckAllSLsAsync()
        {
            await CheckSellLegSLAsync(_state.SellCallLeg, _state.HedgeCallLeg);
            await CheckSellLegSLAsync(_state.SellPutLeg,  _state.HedgePutLeg);
            await CheckFlippedBuyLegSLAsync(_state.SellCallLeg);
            await CheckFlippedBuyLegSLAsync(_state.SellPutLeg);
        }

        /// <summary>
        /// Sell leg SL = CombinedSellEntryPrice.
        /// If LTP >= SL:
        ///   1. Exit sell leg.
        ///   2. Exit its hedge IMMEDIATELY (same tick).
        ///   3. Flip to buy with BuySL%.
        /// </summary>
        private async Task CheckSellLegSLAsync(CalendarLeg sellLeg, CalendarLeg hedgeLeg)
        {
            if (sellLeg.Status != "OPEN" || sellLeg.Action != "SELL") return;
            if (sellLeg.CurrentLTP <= 0 || sellLeg.SLPrice <= 0) return;
            if (sellLeg.CurrentLTP < sellLeg.SLPrice) return;

            _state.Log($"SELL SL hit: {sellLeg.OptionType} " +
                       $"LTP={sellLeg.CurrentLTP:F2} >= SL={sellLeg.SLPrice:F2}");

            // Step 1: Exit sell leg
            await ExitSingleLegAsync(sellLeg, "Sell SL hit");

            // Step 2: Exit hedge IMMEDIATELY (same tick, before flip)
            if (hedgeLeg.Status == "OPEN" && hedgeLeg.IsHedgeLeg)
            {
                _state.Log($"Exiting hedge {hedgeLeg.OptionType} {hedgeLeg.TradingSymbol} " +
                           $"immediately with sell leg.");
                await ExitSingleLegAsync(hedgeLeg, "Sell SL hit — hedge exits with sell");
                hedgeLeg.IsHedgeLeg = false;
            }

            // Step 3: Flip to BUY
            var item = ResolveOptionBySymbol(sellLeg.TradingSymbol);
            if (item == null)
            {
                _state.Log($"ERROR: {sellLeg.TradingSymbol} not found for flip.");
                return;
            }

            double buyEntry = item.LTP;
            double buySL    = buyEntry * (1.0 - _cfg.BuySLPercent / 100.0);

            sellLeg.Action          = "BUY";
            sellLeg.EntryPrice      = buyEntry;
            sellLeg.CurrentLTP      = buyEntry;
            sellLeg.SLPrice         = buySL;
            sellLeg.IsFlippedBuyLeg = true;
            sellLeg.Status          = "OPEN";

            await PlaceLegOrderAsync(sellLeg, "BUY", buyEntry);
            _state.Log($"Flipped {sellLeg.OptionType} → BUY @ {buyEntry:F2}, " +
                       $"BuySL={buySL:F2}");
            RecordEvent("FLIP_BUY",
                $"SL hit on {sellLeg.OptionType} {sellLeg.TradingSymbol}",
                entryPrice: sellLeg.SLPrice, exitPrice: buyEntry,
                pnl: (sellLeg.EntryPrice - sellLeg.SLPrice) * _cfg.TotalQty,
                wasHedged: _state.HedgeBought);
            SaveState();
        }

        /// <summary>
        /// Flipped buy leg SL = entry × (1 - BuySL%).
        /// If LTP &lt;= SL → exit buy → flip back to sell → recalculate combined SL.
        /// No hedge action here — hedge was already exited when sell SL hit.
        /// </summary>
        private async Task CheckFlippedBuyLegSLAsync(CalendarLeg leg)
        {
            if (leg.Status != "OPEN" || !leg.IsFlippedBuyLeg) return;
            if (leg.CurrentLTP <= 0 || leg.SLPrice <= 0) return;
            if (leg.CurrentLTP > leg.SLPrice) return;

            _state.Log($"BUY SL hit: {leg.OptionType} " +
                       $"LTP={leg.CurrentLTP:F2} <= SL={leg.SLPrice:F2}. " +
                       $"Flipping back to SELL.");

            await ExitSingleLegAsync(leg, "Buy SL hit");

            var item = ResolveOptionBySymbol(leg.TradingSymbol);
            if (item == null)
            {
                _state.Log($"ERROR: {leg.TradingSymbol} not found for flip-back.");
                return;
            }

            double sellEntry = item.LTP;
            leg.Action          = "SELL";
            leg.EntryPrice      = sellEntry;
            leg.CurrentLTP      = sellEntry;
            leg.IsFlippedBuyLeg = false;
            leg.Status          = "OPEN";

            CalendarLeg otherSell = leg.OptionType == "CE"
                ? _state.SellPutLeg : _state.SellCallLeg;

            double newCombined          = sellEntry + otherSell.EntryPrice;
            leg.SLPrice                 = newCombined;
            otherSell.SLPrice           = newCombined;
            _state.CombinedSellEntryPrice = newCombined;

            await PlaceLegOrderAsync(leg, "SELL", sellEntry);
            _state.Log($"Flipped {leg.OptionType} → SELL @ {sellEntry:F2}, " +
                       $"NewCombinedSL={newCombined:F2}");
            RecordEvent("FLIP_SELL",
                $"Buy SL hit on {leg.OptionType} {leg.TradingSymbol} → flipped back to SELL",
                entryPrice: leg.EntryPrice, exitPrice: leg.SLPrice,
                pnl: (leg.SLPrice - leg.EntryPrice) * _cfg.TotalQty,
                wasHedged: _state.HedgeBought);
            SaveState();
        }

        // ─────────────────────────────────────────────────────────────────────
        // WEEKLY ROLL
        // ─────────────────────────────────────────────────────────────────────

        private async Task RollWeeklyLegsAsync(double spotPrice, DateTime today)
        {
            _state.Phase = CalendarPhase.WeeklyRollInProgress;
            _state.Log($"Weekly roll. Spot={spotPrice}");

            // Exit sell legs and their hedges together
            await ExitSellLegWithHedgeAsync(_state.SellCallLeg, _state.HedgeCallLeg, "Weekly roll");
            await ExitSellLegWithHedgeAsync(_state.SellPutLeg,  _state.HedgePutLeg,  "Weekly roll");

            // Reset hedge flags for next cycle
            _state.HedgeBought  = false;
            _state.HedgeCallLeg = new CalendarLeg();
            _state.HedgePutLeg  = new CalendarLeg();

            var nextWeekly = GetNearestWeeklyExpiry(today.AddDays(1));
            if (nextWeekly == default)
            {
                _state.Log("ERROR: Next weekly expiry not found.");
                _state.Phase = CalendarPhase.Active;
                return;
            }
            _state.CurrentWeeklyExpiry = nextWeekly;

            double newATM = Math.Round(spotPrice / _cfg.StrikeStep) * _cfg.StrikeStep;

            bool isMonthlyExpiryDay =
                today == _state.MonthlyExpiry.Date ||
                nextWeekly.Date == _state.MonthlyExpiry.Date;

            if (isMonthlyExpiryDay &&
                Math.Abs(newATM - _state.BuyCallLeg.Strike) < 1.0)
            {
                _state.Log($"Monthly expiry: buy strike == new ATM ({newATM}). " +
                           "Skipping sell legs. Holding buys to expiry.");
                _state.Phase = CalendarPhase.MonthlyExpiryDay;
                SaveState();
                return;
            }

            var newSellCall = ResolveOption(newATM, isCall: true,  isWeekly: true);
            var newSellPut  = ResolveOption(newATM, isCall: false, isWeekly: true);

            if (newSellCall == null || newSellPut == null)
            {
                _state.Log("ERROR: New weekly ATM straddle not found.");
                _state.Phase = CalendarPhase.Active;
                return;
            }

            _state.SellCallLeg = new CalendarLeg();
            _state.SellPutLeg  = new CalendarLeg();

            await PlaceLegAsync(_state.SellCallLeg, newSellCall, "SELL");
            await PlaceLegAsync(_state.SellPutLeg,  newSellPut,  "SELL");

            _state.ATMStrike = newATM;
            _state.CombinedSellEntryPrice =
                _state.SellCallLeg.EntryPrice + _state.SellPutLeg.EntryPrice;
            _state.SellCallLeg.SLPrice = _state.CombinedSellEntryPrice;
            _state.SellPutLeg.SLPrice  = _state.CombinedSellEntryPrice;

            _state.Log($"Roll complete. NewATM={newATM}, " +
                       $"CE={_state.SellCallLeg.EntryPrice:F2}, " +
                       $"PE={_state.SellPutLeg.EntryPrice:F2}, " +
                       $"CombinedSL={_state.CombinedSellEntryPrice:F2}");

            _state.Phase = CalendarPhase.Active;
            RecordEvent("ROLL",
                $"Weekly roll → ATM={newATM} CE={_state.SellCallLeg.EntryPrice:N2} PE={_state.SellPutLeg.EntryPrice:N2}",
                entryPrice: _state.CombinedSellEntryPrice);
            SaveState();
        }

        // ─────────────────────────────────────────────────────────────────────
        // MAX PROFIT / MAX LOSS
        // ─────────────────────────────────────────────────────────────────────

        private async Task<bool> CheckMaxProfitLossAsync()
        {
            double pnl = _state.TotalPnL;
            if (pnl >= _cfg.MaxProfit)
            {
                _state.Log($"MAX PROFIT ₹{pnl:F2} hit. Exiting all.");
                await ExitAllAndCompleteAsync("Max Profit");
                return true;
            }
            if (pnl <= -Math.Abs(_cfg.MaxLoss))
            {
                _state.Log($"MAX LOSS ₹{pnl:F2} hit. Exiting all.");
                await ExitAllAndCompleteAsync("Max Loss");
                return true;
            }
            return false;
        }

        // ─────────────────────────────────────────────────────────────────────
        // EXIT HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private async Task ExitAllAndCompleteAsync(string reason)
        {
            _state.Log($"Exiting all legs + hedges: {reason}");

            await ExitSellLegWithHedgeAsync(_state.SellCallLeg, _state.HedgeCallLeg, reason);
            await ExitSellLegWithHedgeAsync(_state.SellPutLeg,  _state.HedgePutLeg,  reason);
            await ExitSingleLegAsync(_state.BuyCallLeg, reason);
            await ExitSingleLegAsync(_state.BuyPutLeg,  reason);

            _state.Phase = CalendarPhase.Completed;
            IsActive     = false;
            _state.Log($"Strategy COMPLETED. P&L=₹{_state.TotalPnL:F2}");
            RecordEvent("EXIT",
                $"Strategy completed — {reason}",
                pnl: _state.TotalRealizedPnL);
            try { _repo?.Delete(_cfg.Name); } catch { /* best-effort */ }
        }

        /// <summary>Exits a sell leg AND its corresponding hedge together.</summary>
        private async Task ExitSellLegWithHedgeAsync(
            CalendarLeg sellLeg, CalendarLeg hedgeLeg, string reason)
        {
            await ExitSingleLegAsync(sellLeg, reason);

            if (hedgeLeg.Status == "OPEN" && hedgeLeg.IsHedgeLeg)
            {
                await ExitSingleLegAsync(hedgeLeg, $"{reason} — hedge exits with sell");
                hedgeLeg.IsHedgeLeg = false;
            }
        }

        /// <summary>Exits a single leg at current LTP, records realized PnL.</summary>
        private async Task ExitSingleLegAsync(CalendarLeg leg, string reason)
        {
            if (leg.Status != "OPEN" || string.IsNullOrEmpty(leg.TradingSymbol)) return;

            string exitAction = leg.Action == "BUY" ? "SELL" : "BUY";
            double exitPrice  = leg.CurrentLTP > 0 ? leg.CurrentLTP : leg.EntryPrice;

            double pnl = leg.Action == "BUY"
                ? (exitPrice - leg.EntryPrice) * _cfg.TotalQty
                : (leg.EntryPrice - exitPrice) * _cfg.TotalQty;

            leg.RealizedPnL         += pnl;
            _state.TotalRealizedPnL += pnl;
            leg.Status               = "EXITED";

            await PlaceLegOrderAsync(leg, exitAction, exitPrice);
            _state.Log($"Exited {leg.Action} {leg.OptionType} {leg.TradingSymbol} " +
                       $"@ {exitPrice:F2} P&L=₹{pnl:F2} [{reason}]");
        }

        // ─────────────────────────────────────────────────────────────────────
        // ORDER PLACEMENT
        // ─────────────────────────────────────────────────────────────────────

        private async Task PlaceLegAsync(CalendarLeg leg, OptionChainItem item, string action)
        {
            leg.TradingSymbol  = item.TradingSymbol;
            leg.Token          = item.Token ?? "";
            leg.OptionType     = item.IsCall ? "CE" : "PE";
            leg.Action         = action;
            leg.Strike         = item.Strike;
            leg.ExpiryDate     = item.ExpiryDate;
            leg.IsWeeklyExpiry = item.IsWeeklyExpiry;
            leg.EntryPrice     = item.LTP;
            leg.CurrentLTP     = item.LTP;
            leg.Status         = "OPEN";

            await PlaceLegOrderAsync(leg, action, item.LTP);
            _state.Log($"{action} {leg.OptionType} {leg.TradingSymbol} " +
                       $"Strike={leg.Strike} Expiry={leg.ExpiryDate:dd-MMM} " +
                       $"@ ₹{leg.EntryPrice:F2}" +
                       (leg.IsHedgeLeg ? " [HEDGE]" : ""));
        }

        private async Task PlaceLegOrderAsync(CalendarLeg leg, string action, double price)
        {
            try
            {
                var sc = new StrategyConfig { Id = 0, Name = _cfg.Name };
                await _engine.ExecuteOrderAsync(sc, leg.TradingSymbol, action, leg.Token, price);

                if (!string.IsNullOrEmpty(leg.Token) &&
                    _engine.SmartStream?.IsConnected == true)
                {
                    _ = _engine.SmartStream.SubscribeAsync(
                        new List<string> { leg.Token }, "NFO");
                }
            }
            catch (Exception ex)
            {
                _state.Log($"Order error {leg.TradingSymbol}: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // LTP UPDATE
        // ─────────────────────────────────────────────────────────────────────

        private void UpdateLegLTPs(TickerData ticker)
        {
            UpdateOneLegLTP(_state.BuyCallLeg,   ticker);
            UpdateOneLegLTP(_state.BuyPutLeg,    ticker);
            UpdateOneLegLTP(_state.SellCallLeg,  ticker);
            UpdateOneLegLTP(_state.SellPutLeg,   ticker);
            UpdateOneLegLTP(_state.HedgeCallLeg, ticker);
            UpdateOneLegLTP(_state.HedgePutLeg,  ticker);
            UpdateUnrealizedPnL();
        }

        private void UpdateOneLegLTP(CalendarLeg leg, TickerData ticker)
        {
            if (leg.Status != "OPEN" || string.IsNullOrEmpty(leg.Token)) return;
            if (ticker.Options != null &&
                ticker.Options.TryGetValue(leg.Token, out var info) && info.Ltp > 0)
                leg.CurrentLTP = info.Ltp;
        }

        private void UpdateUnrealizedPnL()
        {
            double total = 0;
            foreach (var leg in new[] {
                _state.BuyCallLeg,  _state.BuyPutLeg,
                _state.SellCallLeg, _state.SellPutLeg,
                _state.HedgeCallLeg, _state.HedgePutLeg })
            {
                if (leg.Status == "OPEN" && leg.EntryPrice > 0 && leg.CurrentLTP > 0)
                {
                    total += leg.Action == "BUY"
                        ? (leg.CurrentLTP - leg.EntryPrice) * _cfg.TotalQty
                        : (leg.EntryPrice - leg.CurrentLTP) * _cfg.TotalQty;
                }
            }
            _state.TotalUnrealizedPnL = total;
        }

        // ─────────────────────────────────────────────────────────────────────
        // OPTION CHAIN HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private OptionChainItem? ResolveOption(double strike, bool isCall, bool isWeekly)
        {
            var chain = OptionChain;
            if (chain == null || chain.Count == 0)
            {
                _state.Log($"Option chain empty for {_cfg.Symbol}. " +
                           "Refresh chain before starting strategy.");
                return null;
            }
            return chain
                .Where(c => c.IsCall == isCall
                         && Math.Abs(c.Strike - strike) < 1.0
                         && c.IsWeeklyExpiry == isWeekly
                         && c.LTP > 0)
                .OrderBy(c => c.DaysToExpiry)
                .FirstOrDefault();
        }

        /// <summary>Resolves option by exact strike for hedge buying.</summary>
        private OptionChainItem? ResolveOptionByStrike(
            double strike, bool isCall, bool isWeekly)
        {
            var chain = OptionChain;
            if (chain == null) return null;
            return chain
                .Where(c => c.IsCall == isCall
                         && Math.Abs(c.Strike - strike) < 1.0
                         && c.IsWeeklyExpiry == isWeekly
                         && c.LTP > 0)
                .OrderBy(c => c.DaysToExpiry)
                .FirstOrDefault();
        }

        private OptionChainItem? ResolveOptionBySymbol(string tradingSymbol) =>
            OptionChain?.FirstOrDefault(c =>
                c.TradingSymbol == tradingSymbol && c.LTP > 0);

        private DateTime GetNearestWeeklyExpiry(DateTime from) =>
            OptionChain?
                .Where(c => c.IsWeeklyExpiry && c.ExpiryDate.Date >= from.Date)
                .Select(c => c.ExpiryDate.Date)
                .Distinct()
                .OrderBy(d => d)
                .FirstOrDefault() ?? default;

        private DateTime GetNextMonthlyExpiry(DateTime from) =>
            OptionChain?
                .Where(c => !c.IsWeeklyExpiry && c.ExpiryDate.Date >= from.Date)
                .Select(c => c.ExpiryDate.Date)
                .Distinct()
                .OrderBy(d => d)
                .FirstOrDefault() ?? default;

        private static double GetSpotLtp(TickerData ticker, string symbol) =>
            symbol switch
            {
                "BANKNIFTY"  => ticker.BankNifty?.Ltp  ?? 0,
                "FINNIFTY"   => ticker.FinNifty?.Ltp   ?? 0,
                "MIDCPNIFTY" => ticker.MidcpNifty?.Ltp ?? 0,
                "SENSEX"     => ticker.Sensex?.Ltp      ?? 0,
                _            => ticker.Nifty?.Ltp        ?? 0
            };
    }
}
