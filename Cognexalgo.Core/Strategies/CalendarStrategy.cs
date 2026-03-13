using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cognexalgo.Core.Models;
using Cognexalgo.Core.Services;

namespace Cognexalgo.Core.Strategies
{
    /// <summary>
    /// Calendar Spread Strategy Engine.
    ///
    /// Logic summary:
    ///   1. At FirstEntryTime — Buy next-month ATM straddle + Sell nearest-weekly ATM straddle.
    ///   2. Combined sell entry price = SL for each individual sell leg.
    ///   3. If sell CE/PE hits SL — exit sell leg — flip to buy with BuySL%.
    ///      If flipped buy leg hits SL — exit buy — flip back to sell. Repeat indefinitely.
    ///   4. At WeeklyExpiryExitTime — exit all weekly legs — sell next weekly ATM straddle.
    ///   5. On monthly expiry day — if buy strike == new weekly ATM strike, skip sell legs.
    ///      At WeeklyExpiryExitTime — exit everything — strategy Completed.
    ///   6. MaxProfit / MaxLoss — exit all positions immediately.
    /// </summary>
    public class CalendarStrategy : StrategyBase
    {
        private readonly CalendarStrategyConfig _cfg;
        private readonly CalendarStrategyState  _state = new();
        private readonly CandleAggregator       _aggregator;
        private readonly List<Skender.Stock.Indicators.Quote> _candles = new();

        // Cached option chain reference (set from engine.V2 cache)
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

        public CalendarStrategy(TradingEngine engine, CalendarStrategyConfig config)
            : base(engine, "Calendar")
        {
            _cfg  = config;
            Name  = config.Name;

            _aggregator = new CandleAggregator(config.Timeframe);
            _aggregator.OnCandleClosed += candle =>
            {
                _candles.Add(candle);
                if (_candles.Count > 200) _candles.RemoveAt(0);
                _ = OnCandleClosedAsync(candle);
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        // TICK HANDLER
        // ─────────────────────────────────────────────────────────────────────

        public override async Task OnTickAsync(TickerData ticker)
        {
            if (!IsActive || _cfg == null) return;

            double spotLtp = GetSpotLtp(ticker, _cfg.Symbol);
            if (spotLtp <= 0) return;

            var now   = DateTime.Now.TimeOfDay;
            var today = DateTime.Today;

            // Feed aggregator for candle-based SL checks
            _aggregator.AddTick(DateTime.Now, (decimal)spotLtp);

            // Update all open leg LTPs from tick
            UpdateLegLTPs(ticker);

            // ── Phase: Waiting for first entry ────────────────────────────────
            if (_state.Phase == CalendarPhase.WaitingFirstEntry)
            {
                if (now >= _cfg.FirstEntryTime && !_state.FirstEntryDone)
                    await ExecuteFirstEntryAsync(spotLtp, today);
                return;
            }

            // ── Max Profit / Max Loss check ───────────────────────────────────
            UpdateUnrealizedPnL();
            if (await CheckMaxProfitLoss()) return;

            // ── Monthly expiry day exit ───────────────────────────────────────
            if (_state.Phase == CalendarPhase.MonthlyExpiryDay)
            {
                if (now >= _cfg.WeeklyExpiryExitTime)
                    await ExitAllAndCompleteAsync("Monthly expiry exit");
                return;
            }

            // ── Weekly expiry roll ────────────────────────────────────────────
            if (_state.Phase == CalendarPhase.Active &&
                today == _state.CurrentWeeklyExpiry.Date &&
                now >= _cfg.WeeklyExpiryExitTime)
            {
                await RollWeeklyLegsAsync(spotLtp, today);
                return;
            }

            // ── SL checks on every tick (LTP basis) ──────────────────────────
            if (!_cfg.EnableBuySLOnCandleClose)
                await CheckAllSLsAsync(spotLtp);
        }

        // ─────────────────────────────────────────────────────────────────────
        // CANDLE CLOSE HANDLER
        // ─────────────────────────────────────────────────────────────────────

        private async Task OnCandleClosedAsync(Skender.Stock.Indicators.Quote candle)
        {
            if (_state.Phase != CalendarPhase.Active) return;
            if (_cfg.EnableBuySLOnCandleClose)
                await CheckAllSLsAsync((double)candle.Close);
        }

        // ─────────────────────────────────────────────────────────────────────
        // FIRST ENTRY
        // ─────────────────────────────────────────────────────────────────────

        private async Task ExecuteFirstEntryAsync(double spotPrice, DateTime today)
        {
            _state.Log($"First entry triggered. Spot={spotPrice}");
            _state.FirstEntryDone = true;

            double atmStrike = Math.Round(spotPrice / _cfg.StrikeStep) * _cfg.StrikeStep;
            _state.ATMStrike = atmStrike;
            _state.Log($"ATM Strike = {atmStrike}");

            var monthlyExpiry = GetNextMonthlyExpiry(today);
            var weeklyExpiry  = GetNearestWeeklyExpiry(today);

            if (monthlyExpiry == default || weeklyExpiry == default)
            {
                _state.Log("ERROR: Could not resolve expiry dates from option chain. " +
                           "Ensure option chain is loaded.");
                return;
            }

            _state.MonthlyExpiry        = monthlyExpiry;
            _state.CurrentWeeklyExpiry  = weeklyExpiry;
            _state.MonthlyExpiryIsToday = (today == monthlyExpiry.Date);
            _state.Log($"Monthly expiry={monthlyExpiry:dd-MMM-yyyy}, " +
                       $"Weekly expiry={weeklyExpiry:dd-MMM-yyyy}");

            // ── BUY next-month ATM straddle ───────────────────────────────────
            var buyCall = ResolveOption(atmStrike, "CE", isWeekly: false);
            var buyPut  = ResolveOption(atmStrike, "PE", isWeekly: false);

            if (buyCall == null || buyPut == null)
            {
                _state.Log("ERROR: Could not find monthly ATM straddle in option chain.");
                return;
            }

            await PlaceLegAsync(_state.BuyCallLeg, buyCall, "BUY");
            await PlaceLegAsync(_state.BuyPutLeg,  buyPut,  "BUY");

            // ── SELL nearest-weekly ATM straddle ──────────────────────────────
            if (!_state.MonthlyExpiryIsToday)
            {
                var sellCall = ResolveOption(atmStrike, "CE", isWeekly: true);
                var sellPut  = ResolveOption(atmStrike, "PE", isWeekly: true);

                if (sellCall == null || sellPut == null)
                {
                    _state.Log("ERROR: Could not find weekly ATM straddle in option chain.");
                    return;
                }

                await PlaceLegAsync(_state.SellCallLeg, sellCall, "SELL");
                await PlaceLegAsync(_state.SellPutLeg,  sellPut,  "SELL");

                _state.CombinedSellEntryPrice =
                    _state.SellCallLeg.EntryPrice + _state.SellPutLeg.EntryPrice;
                _state.SellCallLeg.SLPrice = _state.CombinedSellEntryPrice;
                _state.SellPutLeg.SLPrice  = _state.CombinedSellEntryPrice;

                _state.Log($"Sell entry: CE={_state.SellCallLeg.EntryPrice:F2}, " +
                           $"PE={_state.SellPutLeg.EntryPrice:F2}, " +
                           $"Combined SL={_state.CombinedSellEntryPrice:F2}");
            }

            _state.Phase = CalendarPhase.Active;
            _state.Log("Strategy is now ACTIVE.");
        }

        // ─────────────────────────────────────────────────────────────────────
        // SL CHECKS
        // ─────────────────────────────────────────────────────────────────────

        private async Task CheckAllSLsAsync(double referencePrice)
        {
            await CheckSellLegSLAsync(_state.SellCallLeg, _state.SellPutLeg, referencePrice);
            await CheckSellLegSLAsync(_state.SellPutLeg,  _state.SellCallLeg, referencePrice);
            await CheckBuyLegSLAsync(_state.SellCallLeg, referencePrice);
            await CheckBuyLegSLAsync(_state.SellPutLeg,  referencePrice);
        }

        private async Task CheckSellLegSLAsync(
            CalendarLeg sellLeg, CalendarLeg otherSellLeg, double referencePrice)
        {
            if (sellLeg.Status != "OPEN" || sellLeg.Action != "SELL") return;
            if (sellLeg.CurrentLTP <= 0) return;

            if (sellLeg.CurrentLTP < sellLeg.SLPrice) return;

            _state.Log($"SL hit on SELL {sellLeg.OptionType}! " +
                       $"LTP={sellLeg.CurrentLTP:F2} >= SL={sellLeg.SLPrice:F2}. " +
                       $"Exiting sell → flipping to BUY.");

            await ExitLegAsync(sellLeg, "SL hit");

            var chainItem = ResolveOptionBySymbol(sellLeg.TradingSymbol);
            if (chainItem == null)
            {
                _state.Log($"ERROR: Could not find {sellLeg.TradingSymbol} in chain to flip to buy.");
                return;
            }

            sellLeg.Action            = "BUY";
            sellLeg.EntryPrice        = chainItem.LTP;
            sellLeg.CurrentLTP        = chainItem.LTP;
            sellLeg.IsFlippedBuyLeg   = true;
            sellLeg.FlippedBuySLPrice = chainItem.LTP * (1 - _cfg.BuySLPercent / 100.0);
            sellLeg.SLPrice           = sellLeg.FlippedBuySLPrice;
            sellLeg.Status            = "OPEN";

            await PlaceLegOrderAsync(sellLeg, "BUY", chainItem.LTP);
            _state.Log($"Flipped {sellLeg.OptionType} to BUY @ {chainItem.LTP:F2}, " +
                       $"BuySL={sellLeg.SLPrice:F2}");
        }

        private async Task CheckBuyLegSLAsync(CalendarLeg leg, double referencePrice)
        {
            if (leg.Status != "OPEN" || !leg.IsFlippedBuyLeg) return;
            if (leg.CurrentLTP <= 0) return;

            if (leg.CurrentLTP > leg.SLPrice) return;

            _state.Log($"BuySL hit on flipped BUY {leg.OptionType}! " +
                       $"LTP={leg.CurrentLTP:F2} <= SL={leg.SLPrice:F2}. " +
                       $"Exiting buy → flipping back to SELL.");

            await ExitLegAsync(leg, "BuySL hit");

            var chainItem = ResolveOptionBySymbol(leg.TradingSymbol);
            if (chainItem == null)
            {
                _state.Log($"ERROR: Could not find {leg.TradingSymbol} to flip back to SELL.");
                return;
            }

            leg.Action          = "SELL";
            leg.EntryPrice      = chainItem.LTP;
            leg.CurrentLTP      = chainItem.LTP;
            leg.IsFlippedBuyLeg = false;

            double combinedEntry = leg.EntryPrice +
                (leg.OptionType == "CE"
                    ? _state.SellPutLeg.EntryPrice
                    : _state.SellCallLeg.EntryPrice);
            leg.SLPrice                   = combinedEntry;
            _state.CombinedSellEntryPrice = combinedEntry;

            if (leg.OptionType == "CE") _state.SellPutLeg.SLPrice  = combinedEntry;
            else                        _state.SellCallLeg.SLPrice = combinedEntry;

            leg.Status = "OPEN";
            await PlaceLegOrderAsync(leg, "SELL", chainItem.LTP);
            _state.Log($"Flipped {leg.OptionType} back to SELL @ {chainItem.LTP:F2}, " +
                       $"New combined SL={combinedEntry:F2}");
        }

        // ─────────────────────────────────────────────────────────────────────
        // WEEKLY ROLL
        // ─────────────────────────────────────────────────────────────────────

        private async Task RollWeeklyLegsAsync(double spotPrice, DateTime today)
        {
            _state.Phase = CalendarPhase.WeeklyRollInProgress;
            _state.Log($"Weekly expiry roll triggered. Spot={spotPrice}");

            await ExitLegAsync(_state.SellCallLeg, "Weekly expiry roll");
            await ExitLegAsync(_state.SellPutLeg,  "Weekly expiry roll");

            var nextWeekly = GetNearestWeeklyExpiry(today.AddDays(1));
            if (nextWeekly == default)
            {
                _state.Log("ERROR: Could not find next weekly expiry.");
                _state.Phase = CalendarPhase.Active;
                return;
            }

            _state.CurrentWeeklyExpiry = nextWeekly;
            _state.Log($"Next weekly expiry = {nextWeekly:dd-MMM-yyyy}");

            double newATM = Math.Round(spotPrice / _cfg.StrikeStep) * _cfg.StrikeStep;

            bool isMonthlyExpiryDay = (today == _state.MonthlyExpiry.Date) ||
                                       (nextWeekly.Date == _state.MonthlyExpiry.Date);

            if (isMonthlyExpiryDay && Math.Abs(newATM - _state.BuyCallLeg.Strike) < 1.0)
            {
                _state.Log($"Monthly expiry: Buy strike ({_state.BuyCallLeg.Strike}) == " +
                           $"new weekly ATM ({newATM}). Skipping sell legs. " +
                           $"Holding buy legs to expiry.");
                _state.Phase = CalendarPhase.MonthlyExpiryDay;
                return;
            }

            var newSellCall = ResolveOption(newATM, "CE", isWeekly: true);
            var newSellPut  = ResolveOption(newATM, "PE", isWeekly: true);

            if (newSellCall == null || newSellPut == null)
            {
                _state.Log("ERROR: Could not find new weekly ATM straddle.");
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

            _state.Log($"Weekly roll complete. New ATM={newATM}, " +
                       $"CE={_state.SellCallLeg.EntryPrice:F2}, " +
                       $"PE={_state.SellPutLeg.EntryPrice:F2}, " +
                       $"New combined SL={_state.CombinedSellEntryPrice:F2}");

            _state.Phase = CalendarPhase.Active;
        }

        // ─────────────────────────────────────────────────────────────────────
        // MAX PROFIT / MAX LOSS
        // ─────────────────────────────────────────────────────────────────────

        private async Task<bool> CheckMaxProfitLoss()
        {
            double totalPnL = _state.TotalPnL;

            if (totalPnL >= _cfg.MaxProfit)
            {
                _state.Log($"MAX PROFIT hit: ₹{totalPnL:F2} >= ₹{_cfg.MaxProfit:F2}. Exiting all.");
                await ExitAllAndCompleteAsync("Max Profit");
                return true;
            }

            if (totalPnL <= -Math.Abs(_cfg.MaxLoss))
            {
                _state.Log($"MAX LOSS hit: ₹{totalPnL:F2} <= -₹{_cfg.MaxLoss:F2}. Exiting all.");
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
            _state.Log($"Exiting all legs. Reason: {reason}");
            await ExitLegAsync(_state.BuyCallLeg,  reason);
            await ExitLegAsync(_state.BuyPutLeg,   reason);
            await ExitLegAsync(_state.SellCallLeg, reason);
            await ExitLegAsync(_state.SellPutLeg,  reason);
            _state.Phase = CalendarPhase.Completed;
            IsActive     = false;
            _state.Log($"Strategy COMPLETED. Total P&L = ₹{_state.TotalPnL:F2}");
        }

        private async Task ExitLegAsync(CalendarLeg leg, string reason)
        {
            if (leg.Status != "OPEN" || string.IsNullOrEmpty(leg.TradingSymbol)) return;

            string exitAction = leg.Action == "BUY" ? "SELL" : "BUY";
            double exitPrice  = leg.CurrentLTP > 0 ? leg.CurrentLTP : leg.EntryPrice;

            await PlaceLegOrderAsync(leg, exitAction, exitPrice);

            double pnl = leg.Action == "BUY"
                ? (exitPrice - leg.EntryPrice) * _cfg.TotalQty
                : (leg.EntryPrice - exitPrice) * _cfg.TotalQty;

            leg.RealizedPnL         += pnl;
            _state.TotalRealizedPnL += pnl;
            leg.Status = "EXITED";
            _state.Log($"Exited {leg.Action} {leg.OptionType} {leg.TradingSymbol} " +
                       $"@ {exitPrice:F2}. P&L=₹{pnl:F2}. Reason={reason}");
        }

        // ─────────────────────────────────────────────────────────────────────
        // ORDER PLACEMENT
        // ─────────────────────────────────────────────────────────────────────

        private async Task PlaceLegAsync(CalendarLeg leg, OptionChainItem item, string action)
        {
            leg.TradingSymbol = item.TradingSymbol;   // alias → item.Symbol
            leg.Token         = item.Token ?? "";
            leg.OptionType    = item.IsCall ? "CE" : "PE";
            leg.Action        = action;
            leg.Strike        = item.Strike;
            leg.Expiry        = item.ExpiryDate;
            leg.IsWeekly      = item.IsWeeklyExpiry;
            leg.EntryPrice    = item.LTP;
            leg.CurrentLTP    = item.LTP;
            leg.Status        = "OPEN";

            await PlaceLegOrderAsync(leg, action, item.LTP);
            _state.Log($"Placed {action} {leg.OptionType} {leg.TradingSymbol} " +
                       $"Strike={leg.Strike} Expiry={leg.Expiry:dd-MMM} @ ₹{leg.EntryPrice:F2}");
        }

        private async Task PlaceLegOrderAsync(CalendarLeg leg, string action, double price)
        {
            try
            {
                string orderId = await _engine.Api.PlaceOrderAsync(
                    symbol:          leg.TradingSymbol,
                    token:           leg.Token,
                    transactionType: action,
                    qty:             _cfg.TotalQty,
                    price:           price,
                    variety:         "NORMAL",
                    productType:     "MIS",
                    exchange:        "NFO");

                leg.OrderId = orderId ?? "";

                if (!string.IsNullOrEmpty(leg.Token) && _engine.SmartStream?.IsConnected == true)
                    _ = _engine.SmartStream.SubscribeAsync(
                        new System.Collections.Generic.List<string> { leg.Token }, "NFO");
            }
            catch (Exception ex)
            {
                _state.Log($"ERROR placing order for {leg.TradingSymbol}: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // LTP UPDATE
        // ─────────────────────────────────────────────────────────────────────

        private void UpdateLegLTPs(TickerData ticker)
        {
            UpdateLegLTP(_state.BuyCallLeg,  ticker);
            UpdateLegLTP(_state.BuyPutLeg,   ticker);
            UpdateLegLTP(_state.SellCallLeg, ticker);
            UpdateLegLTP(_state.SellPutLeg,  ticker);
            UpdateUnrealizedPnL();
        }

        private void UpdateLegLTP(CalendarLeg leg, TickerData ticker)
        {
            if (leg.Status != "OPEN" || string.IsNullOrEmpty(leg.Token)) return;

            if (ticker.Options != null &&
                ticker.Options.TryGetValue(leg.Token, out var info) && info.Ltp > 0)
            {
                leg.CurrentLTP = info.Ltp;
            }
        }

        private void UpdateUnrealizedPnL()
        {
            double unrealized = 0;
            foreach (var leg in new[] {
                _state.BuyCallLeg, _state.BuyPutLeg,
                _state.SellCallLeg, _state.SellPutLeg })
            {
                if (leg.Status == "OPEN" && leg.EntryPrice > 0 && leg.CurrentLTP > 0)
                {
                    double legPnl = leg.Action == "BUY"
                        ? (leg.CurrentLTP - leg.EntryPrice) * _cfg.TotalQty
                        : (leg.EntryPrice - leg.CurrentLTP) * _cfg.TotalQty;
                    unrealized += legPnl;
                }
            }
            _state.TotalUnrealizedPnL = unrealized;
        }

        // ─────────────────────────────────────────────────────────────────────
        // OPTION CHAIN HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private OptionChainItem? ResolveOption(double strike, string optionType, bool isWeekly)
        {
            var chain = OptionChain;
            if (chain == null || chain.Count == 0) return null;

            bool isCall = optionType == "CE";
            return chain
                .Where(c => c.IsCall == isCall
                         && Math.Abs(c.Strike - strike) < 1.0
                         && c.IsWeeklyExpiry == isWeekly
                         && c.LTP > 0)
                .OrderBy(c => c.DaysToExpiry)
                .FirstOrDefault();
        }

        private OptionChainItem? ResolveOptionBySymbol(string tradingSymbol)
        {
            return OptionChain?.FirstOrDefault(c =>
                c.TradingSymbol == tradingSymbol && c.LTP > 0);
        }

        private DateTime GetNearestWeeklyExpiry(DateTime from)
        {
            return OptionChain?
                .Where(c => c.IsWeeklyExpiry && c.ExpiryDate.Date >= from.Date)
                .Select(c => c.ExpiryDate.Date)
                .Distinct()
                .OrderBy(d => d)
                .FirstOrDefault() ?? default;
        }

        private DateTime GetNextMonthlyExpiry(DateTime from)
        {
            return OptionChain?
                .Where(c => !c.IsWeeklyExpiry && c.ExpiryDate.Date >= from.Date)
                .Select(c => c.ExpiryDate.Date)
                .Distinct()
                .OrderBy(d => d)
                .FirstOrDefault() ?? default;
        }

        private double GetSpotLtp(TickerData ticker, string symbol)
        {
            return symbol switch
            {
                "BANKNIFTY"  => ticker.BankNifty?.Ltp  ?? 0,
                "FINNIFTY"   => ticker.FinNifty?.Ltp   ?? 0,
                "MIDCPNIFTY" => ticker.MidcpNifty?.Ltp ?? 0,
                "SENSEX"     => ticker.Sensex?.Ltp      ?? 0,
                _            => ticker.Nifty?.Ltp        ?? 0
            };
        }
    }
}
