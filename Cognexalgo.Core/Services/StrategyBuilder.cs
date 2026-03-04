using System;
using System.Collections.Generic;
using Cognexalgo.Core.Models;

namespace Cognexalgo.Core.Services
{
    /// <summary>
    /// Builds standard option strategy leg structures from a strategy name and spot price.
    /// Fixed offsets (100, 200, 300, 400 pts) are used for all strategies.
    /// </summary>
    public class StrategyBuilder
    {
        /// <summary>
        /// Returns the legs for the named strategy with strikes auto-calculated from <paramref name="spot"/>.
        /// </summary>
        /// <param name="strategyName">Strategy name as returned by StrategyRepository.</param>
        /// <param name="spot">Current spot/LTP price of the underlying.</param>
        /// <param name="symbol">Underlying symbol used to resolve ATM strike step (default: NIFTY).</param>
        public List<OptionLeg> BuildStrategy(string strategyName, decimal spot, string symbol = "NIFTY")
        {
            decimal atm  = StrikeCalculator.GetATMStrike(spot, symbol);
            DateTime near = DateTime.Today.AddDays(7);
            DateTime far  = DateTime.Today.AddDays(30);

            return strategyName switch
            {
                // ── Neutral ────────────────────────────────────────────────────────
                // Sell ATM Call + Sell ATM Put
                "Short Straddle" => new List<OptionLeg>
                {
                    Leg(OptionType.Call, ActionType.Sell, atm, near),
                    Leg(OptionType.Put,  ActionType.Sell, atm, near),
                },
                // Buy ATM Call + Buy ATM Put
                "Long Straddle" => new List<OptionLeg>
                {
                    Leg(OptionType.Call, ActionType.Buy, atm, near),
                    Leg(OptionType.Put,  ActionType.Buy, atm, near),
                },
                // Sell Call ATM+200 + Sell Put ATM-200
                "Short Strangle" => new List<OptionLeg>
                {
                    Leg(OptionType.Call, ActionType.Sell, atm + 200, near),
                    Leg(OptionType.Put,  ActionType.Sell, atm - 200, near),
                },
                // Buy Call ATM+200 + Buy Put ATM-200
                "Long Strangle" => new List<OptionLeg>
                {
                    Leg(OptionType.Call, ActionType.Buy, atm + 200, near),
                    Leg(OptionType.Put,  ActionType.Buy, atm - 200, near),
                },
                // Buy Put ATM-400 | Sell Put ATM-200 | Sell Call ATM+200 | Buy Call ATM+400
                "Short Iron Condor" => new List<OptionLeg>
                {
                    Leg(OptionType.Put,  ActionType.Buy,  atm - 400, near),
                    Leg(OptionType.Put,  ActionType.Sell, atm - 200, near),
                    Leg(OptionType.Call, ActionType.Sell, atm + 200, near),
                    Leg(OptionType.Call, ActionType.Buy,  atm + 400, near),
                },
                // Inverse: Sell Put ATM-400 | Buy Put ATM-200 | Buy Call ATM+200 | Sell Call ATM+400
                "Long Iron Condor" => new List<OptionLeg>
                {
                    Leg(OptionType.Put,  ActionType.Sell, atm - 400, near),
                    Leg(OptionType.Put,  ActionType.Buy,  atm - 200, near),
                    Leg(OptionType.Call, ActionType.Buy,  atm + 200, near),
                    Leg(OptionType.Call, ActionType.Sell, atm + 400, near),
                },
                // Sell Call ATM | Sell Put ATM | Buy Call ATM+200 | Buy Put ATM-200
                "Short Iron Butterfly" => new List<OptionLeg>
                {
                    Leg(OptionType.Put,  ActionType.Buy,  atm - 200, near),
                    Leg(OptionType.Put,  ActionType.Sell, atm,       near),
                    Leg(OptionType.Call, ActionType.Sell, atm,       near),
                    Leg(OptionType.Call, ActionType.Buy,  atm + 200, near),
                },
                // Buy Call ATM | Buy Put ATM | Sell Call ATM+200 | Sell Put ATM-200
                "Long Iron Butterfly" => new List<OptionLeg>
                {
                    Leg(OptionType.Put,  ActionType.Sell, atm - 200, near),
                    Leg(OptionType.Put,  ActionType.Buy,  atm,       near),
                    Leg(OptionType.Call, ActionType.Buy,  atm,       near),
                    Leg(OptionType.Call, ActionType.Sell, atm + 200, near),
                },
                // Sell Put ATM | Sell Call ATM | Buy Put ATM-400 | Buy Call ATM+400
                "Double Plateau" => new List<OptionLeg>
                {
                    Leg(OptionType.Put,  ActionType.Sell, atm,       near),
                    Leg(OptionType.Call, ActionType.Sell, atm,       near),
                    Leg(OptionType.Put,  ActionType.Buy,  atm - 400, near),
                    Leg(OptionType.Call, ActionType.Buy,  atm + 400, near),
                },
                // Sell Put ATM-200 | Sell Call ATM+100 | Buy Call ATM+300
                "Jade Lizard" => new List<OptionLeg>
                {
                    Leg(OptionType.Put,  ActionType.Sell, atm - 200, near),
                    Leg(OptionType.Call, ActionType.Sell, atm + 100, near),
                    Leg(OptionType.Call, ActionType.Buy,  atm + 300, near),
                },
                // Sell Call ATM+200 | Sell Put ATM-100 | Buy Put ATM-300
                "Reverse Jade Lizard" => new List<OptionLeg>
                {
                    Leg(OptionType.Call, ActionType.Sell, atm + 200, near),
                    Leg(OptionType.Put,  ActionType.Sell, atm - 100, near),
                    Leg(OptionType.Put,  ActionType.Buy,  atm - 300, near),
                },
                // Buy 1 Call ATM | Sell 2 Call ATM+200
                "Call Ratio Spread" => new List<OptionLeg>
                {
                    Leg(OptionType.Call, ActionType.Buy,  atm,       near, lots: 1),
                    Leg(OptionType.Call, ActionType.Sell, atm + 200, near, lots: 2),
                },
                // Buy 1 Put ATM | Sell 2 Put ATM-200
                "Put Ratio Spread" => new List<OptionLeg>
                {
                    Leg(OptionType.Put, ActionType.Buy,  atm,       near, lots: 1),
                    Leg(OptionType.Put, ActionType.Sell, atm - 200, near, lots: 2),
                },
                // Buy Put ATM-800 | Sell Put ATM-400 | Sell ATM Straddle | Sell Call ATM+400 | Buy Call ATM+800
                "Batman Strategy" => new List<OptionLeg>
                {
                    Leg(OptionType.Put,  ActionType.Buy,  atm - 800, near),
                    Leg(OptionType.Put,  ActionType.Sell, atm - 400, near),
                    Leg(OptionType.Put,  ActionType.Sell, atm,       near),
                    Leg(OptionType.Call, ActionType.Sell, atm,       near),
                    Leg(OptionType.Call, ActionType.Sell, atm + 400, near),
                    Leg(OptionType.Call, ActionType.Buy,  atm + 800, near),
                },

                // ── Bearish ────────────────────────────────────────────────────────
                "Short Call" => new List<OptionLeg>
                {
                    Leg(OptionType.Call, ActionType.Sell, atm, near),
                },
                "Long Call" => new List<OptionLeg>
                {
                    Leg(OptionType.Call, ActionType.Buy, atm, near),
                },
                // Sell Call ATM | Buy Call ATM+200
                "Bear Call Spread" => new List<OptionLeg>
                {
                    Leg(OptionType.Call, ActionType.Sell, atm,       near),
                    Leg(OptionType.Call, ActionType.Buy,  atm + 200, near),
                },
                // Buy Put ATM | Sell Put ATM-200
                "Bear Put Spread" => new List<OptionLeg>
                {
                    Leg(OptionType.Put, ActionType.Buy,  atm,       near),
                    Leg(OptionType.Put, ActionType.Sell, atm - 200, near),
                },
                "Long Calendar With Puts" => new List<OptionLeg>
                {
                    Leg(OptionType.Put, ActionType.Buy,  atm, far),
                    Leg(OptionType.Put, ActionType.Sell, atm, near),
                },
                // Sell Call ATM | Buy Call ATM+200 | Sell Call ATM+400 | Buy Call ATM+600
                "Bear Condor" => new List<OptionLeg>
                {
                    Leg(OptionType.Call, ActionType.Sell, atm,       near),
                    Leg(OptionType.Call, ActionType.Buy,  atm + 200, near),
                    Leg(OptionType.Call, ActionType.Sell, atm + 400, near),
                    Leg(OptionType.Call, ActionType.Buy,  atm + 600, near),
                },
                // Buy 1 Call ATM | Sell 2 Call ATM+200 | Buy 1 Call ATM+400
                "Bear Butterfly" => new List<OptionLeg>
                {
                    Leg(OptionType.Call, ActionType.Buy,  atm,       near, lots: 1),
                    Leg(OptionType.Call, ActionType.Sell, atm + 200, near, lots: 2),
                    Leg(OptionType.Call, ActionType.Buy,  atm + 400, near, lots: 1),
                },
                // Sell Call ATM+200 | Buy Put ATM-200
                "Risk Reversal" => new List<OptionLeg>
                {
                    Leg(OptionType.Put,  ActionType.Buy,  atm - 200, near),
                    Leg(OptionType.Call, ActionType.Sell, atm + 200, near),
                },

                // ── Bullish ────────────────────────────────────────────────────────
                "Short Put" => new List<OptionLeg>
                {
                    Leg(OptionType.Put, ActionType.Sell, atm, near),
                },
                // Buy Call ATM | Sell Call ATM+200
                "Bull Call Spread" => new List<OptionLeg>
                {
                    Leg(OptionType.Call, ActionType.Buy,  atm,       near),
                    Leg(OptionType.Call, ActionType.Sell, atm + 200, near),
                },
                // Sell Put ATM | Buy Put ATM-200
                "Bull Put Spread" => new List<OptionLeg>
                {
                    Leg(OptionType.Put, ActionType.Sell, atm,       near),
                    Leg(OptionType.Put, ActionType.Buy,  atm - 200, near),
                },
                "Long Calendar With Calls" => new List<OptionLeg>
                {
                    Leg(OptionType.Call, ActionType.Buy,  atm, far),
                    Leg(OptionType.Call, ActionType.Sell, atm, near),
                },
                // Sell Put ATM | Buy Put ATM-200 | Sell Put ATM-400 | Buy Put ATM-600
                "Bull Condor" => new List<OptionLeg>
                {
                    Leg(OptionType.Put, ActionType.Sell, atm,       near),
                    Leg(OptionType.Put, ActionType.Buy,  atm - 200, near),
                    Leg(OptionType.Put, ActionType.Sell, atm - 400, near),
                    Leg(OptionType.Put, ActionType.Buy,  atm - 600, near),
                },
                // Buy 1 Put ATM | Sell 2 Put ATM-200 | Buy 1 Put ATM-400
                "Bull Butterfly" => new List<OptionLeg>
                {
                    Leg(OptionType.Put, ActionType.Buy,  atm,       near, lots: 1),
                    Leg(OptionType.Put, ActionType.Sell, atm - 200, near, lots: 2),
                    Leg(OptionType.Put, ActionType.Buy,  atm - 400, near, lots: 1),
                },
                // Buy Call ATM+200 | Sell Put ATM-200
                "Range Forward" => new List<OptionLeg>
                {
                    Leg(OptionType.Call, ActionType.Buy,  atm + 200, near),
                    Leg(OptionType.Put,  ActionType.Sell, atm - 200, near),
                },

                _ => throw new ArgumentException($"Unknown strategy '{strategyName}'.", nameof(strategyName)),
            };
        }

        private static OptionLeg Leg(
            OptionType optionType,
            ActionType position,
            decimal strike,
            DateTime expiry,
            int lots = 1) => new OptionLeg
            {
                OptionType = optionType,
                Position   = position,
                Strike     = strike,
                LotSize    = lots,
                Premium    = 0m,
                Expiry     = expiry,
            };
    }
}
