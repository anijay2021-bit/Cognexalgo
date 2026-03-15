namespace Cognexalgo.UI.Services
{
    public class LegFormula
    {
        public string Action       { get; set; } = "";  // "BUY" | "SELL"
        public string Type         { get; set; } = "";  // "CE"  | "PE"
        public int    StrikeOffset { get; set; }        // points from ATM  (0=ATM, +200=ATM+200, -300=ATM-300)
    }

    public class StrategyTemplate
    {
        public string       Name     { get; set; } = "";
        public string       Category { get; set; } = "";
        public LegFormula[] Legs     { get; set; } = Array.Empty<LegFormula>();
    }

    public static class StrategyRepository
    {
        public static List<string> GetStrategies(string bias) => bias switch
        {
            "Neutral" => new List<string>
            {
                "Short Straddle",
                "Long Straddle",
                "Short Strangle",
                "Long Strangle",
                "Long Iron Condor",
                "Short Iron Condor",
                "Short Iron Butterfly",
                "Long Iron Butterfly",
                "Double Plateau",
                "Jade Lizard",
                "Reverse Jade Lizard",
                "Call Ratio Spread",
                "Put Ratio Spread",
                "Batman Strategy"
            },
            "Bearish" => new List<string>
            {
                "Short Call",
                "Long Put",
                "Bear Call Spread",
                "Bear Put Spread",
                "Bear Condor",
                "Bear Butterfly",
                "Put Ratio Spread",
                "Risk Reversal"
            },
            "Bullish" => new List<string>
            {
                "Short Put",
                "Long Call",
                "Bull Call Spread",
                "Bull Put Spread",
                "Bull Condor",
                "Bull Butterfly",
                "Call Ratio Spread",
                "Range Forward"
            },
            _ => new List<string>()
        };

        // ── Template leg definitions ──────────────────────────────────────────────
        // StrikeOffset is in index points from ATM.
        // Typical for NIFTY (step=50): ±100 = 2 strikes OTM, ±200 = 4 strikes OTM.
        // User can adjust after loading.

        private static readonly Dictionary<string, StrategyTemplate> _templates = new()
        {
            // ── Neutral ───────────────────────────────────────────────────────────

            ["Short Straddle"] = new StrategyTemplate
            {
                Name = "Short Straddle", Category = "Neutral",
                Legs = new[]
                {
                    new LegFormula { Action = "SELL", Type = "CE", StrikeOffset =    0 },
                    new LegFormula { Action = "SELL", Type = "PE", StrikeOffset =    0 },
                }
            },
            ["Long Straddle"] = new StrategyTemplate
            {
                Name = "Long Straddle", Category = "Neutral",
                Legs = new[]
                {
                    new LegFormula { Action = "BUY",  Type = "CE", StrikeOffset =    0 },
                    new LegFormula { Action = "BUY",  Type = "PE", StrikeOffset =    0 },
                }
            },
            ["Short Strangle"] = new StrategyTemplate
            {
                Name = "Short Strangle", Category = "Neutral",
                Legs = new[]
                {
                    new LegFormula { Action = "SELL", Type = "CE", StrikeOffset = +300 },
                    new LegFormula { Action = "SELL", Type = "PE", StrikeOffset = -300 },
                }
            },
            ["Long Strangle"] = new StrategyTemplate
            {
                Name = "Long Strangle", Category = "Neutral",
                Legs = new[]
                {
                    new LegFormula { Action = "BUY",  Type = "CE", StrikeOffset = +300 },
                    new LegFormula { Action = "BUY",  Type = "PE", StrikeOffset = -300 },
                }
            },
            // Short Iron Condor = sell inner strikes, buy outer wings (net credit)
            ["Short Iron Condor"] = new StrategyTemplate
            {
                Name = "Short Iron Condor", Category = "Neutral",
                Legs = new[]
                {
                    new LegFormula { Action = "SELL", Type = "CE", StrikeOffset = +300 },
                    new LegFormula { Action = "BUY",  Type = "CE", StrikeOffset = +400 },
                    new LegFormula { Action = "SELL", Type = "PE", StrikeOffset = -300 },
                    new LegFormula { Action = "BUY",  Type = "PE", StrikeOffset = -400 },
                }
            },
            // Long Iron Condor = buy inner strikes, sell outer wings (net debit)
            ["Long Iron Condor"] = new StrategyTemplate
            {
                Name = "Long Iron Condor", Category = "Neutral",
                Legs = new[]
                {
                    new LegFormula { Action = "BUY",  Type = "CE", StrikeOffset = +300 },
                    new LegFormula { Action = "SELL", Type = "CE", StrikeOffset = +400 },
                    new LegFormula { Action = "BUY",  Type = "PE", StrikeOffset = -300 },
                    new LegFormula { Action = "SELL", Type = "PE", StrikeOffset = -400 },
                }
            },
            // Short Iron Butterfly = sell ATM straddle, buy OTM wings (net credit)
            ["Short Iron Butterfly"] = new StrategyTemplate
            {
                Name = "Short Iron Butterfly", Category = "Neutral",
                Legs = new[]
                {
                    new LegFormula { Action = "SELL", Type = "CE", StrikeOffset =    0 },
                    new LegFormula { Action = "BUY",  Type = "CE", StrikeOffset = +200 },
                    new LegFormula { Action = "SELL", Type = "PE", StrikeOffset =    0 },
                    new LegFormula { Action = "BUY",  Type = "PE", StrikeOffset = -200 },
                }
            },
            // Long Iron Butterfly = buy ATM straddle, sell OTM wings (net debit)
            ["Long Iron Butterfly"] = new StrategyTemplate
            {
                Name = "Long Iron Butterfly", Category = "Neutral",
                Legs = new[]
                {
                    new LegFormula { Action = "BUY",  Type = "CE", StrikeOffset =    0 },
                    new LegFormula { Action = "SELL", Type = "CE", StrikeOffset = +200 },
                    new LegFormula { Action = "BUY",  Type = "PE", StrikeOffset =    0 },
                    new LegFormula { Action = "SELL", Type = "PE", StrikeOffset = -200 },
                }
            },
            // Double Plateau = short strangle with wider protective wings
            ["Double Plateau"] = new StrategyTemplate
            {
                Name = "Double Plateau", Category = "Neutral",
                Legs = new[]
                {
                    new LegFormula { Action = "SELL", Type = "CE", StrikeOffset = +200 },
                    new LegFormula { Action = "SELL", Type = "PE", StrikeOffset = -200 },
                    new LegFormula { Action = "BUY",  Type = "CE", StrikeOffset = +400 },
                    new LegFormula { Action = "BUY",  Type = "PE", StrikeOffset = -400 },
                }
            },
            // Jade Lizard = sell OTM call + sell OTM put spread (no upside risk)
            ["Jade Lizard"] = new StrategyTemplate
            {
                Name = "Jade Lizard", Category = "Neutral",
                Legs = new[]
                {
                    new LegFormula { Action = "SELL", Type = "CE", StrikeOffset = +300 },
                    new LegFormula { Action = "SELL", Type = "PE", StrikeOffset = -100 },
                    new LegFormula { Action = "BUY",  Type = "PE", StrikeOffset = -300 },
                }
            },
            // Reverse Jade Lizard = buy OTM call + buy OTM put spread
            ["Reverse Jade Lizard"] = new StrategyTemplate
            {
                Name = "Reverse Jade Lizard", Category = "Neutral",
                Legs = new[]
                {
                    new LegFormula { Action = "BUY",  Type = "CE", StrikeOffset = +300 },
                    new LegFormula { Action = "BUY",  Type = "PE", StrikeOffset = -100 },
                    new LegFormula { Action = "SELL", Type = "PE", StrikeOffset = -300 },
                }
            },
            // Call Ratio Spread = buy 1 ATM call, sell 2 OTM calls (net credit; bearish above breakeven)
            ["Call Ratio Spread"] = new StrategyTemplate
            {
                Name = "Call Ratio Spread", Category = "Neutral",
                Legs = new[]
                {
                    new LegFormula { Action = "BUY",  Type = "CE", StrikeOffset =    0 },
                    new LegFormula { Action = "SELL", Type = "CE", StrikeOffset = +100 },
                    new LegFormula { Action = "SELL", Type = "CE", StrikeOffset = +200 },
                }
            },
            // Put Ratio Spread = buy 1 ATM put, sell 2 OTM puts
            ["Put Ratio Spread"] = new StrategyTemplate
            {
                Name = "Put Ratio Spread", Category = "Neutral",
                Legs = new[]
                {
                    new LegFormula { Action = "BUY",  Type = "PE", StrikeOffset =    0 },
                    new LegFormula { Action = "SELL", Type = "PE", StrikeOffset = -100 },
                    new LegFormula { Action = "SELL", Type = "PE", StrikeOffset = -200 },
                }
            },
            // Batman = two short butterflies (double-peaked profit tent)
            ["Batman Strategy"] = new StrategyTemplate
            {
                Name = "Batman Strategy", Category = "Neutral",
                Legs = new[]
                {
                    new LegFormula { Action = "BUY",  Type = "CE", StrikeOffset =    0 },
                    new LegFormula { Action = "SELL", Type = "CE", StrikeOffset = +100 },
                    new LegFormula { Action = "SELL", Type = "CE", StrikeOffset = +300 },
                    new LegFormula { Action = "BUY",  Type = "CE", StrikeOffset = +400 },
                    new LegFormula { Action = "BUY",  Type = "PE", StrikeOffset =    0 },
                    new LegFormula { Action = "SELL", Type = "PE", StrikeOffset = -100 },
                    new LegFormula { Action = "SELL", Type = "PE", StrikeOffset = -300 },
                    new LegFormula { Action = "BUY",  Type = "PE", StrikeOffset = -400 },
                }
            },

            // ── Bullish ───────────────────────────────────────────────────────────

            ["Short Put"] = new StrategyTemplate
            {
                Name = "Short Put", Category = "Bullish",
                Legs = new[]
                {
                    new LegFormula { Action = "SELL", Type = "PE", StrikeOffset = -200 },
                }
            },
            ["Long Call"] = new StrategyTemplate
            {
                Name = "Long Call", Category = "Bullish",
                Legs = new[]
                {
                    new LegFormula { Action = "BUY",  Type = "CE", StrikeOffset =    0 },
                }
            },
            ["Bull Call Spread"] = new StrategyTemplate
            {
                Name = "Bull Call Spread", Category = "Bullish",
                Legs = new[]
                {
                    new LegFormula { Action = "BUY",  Type = "CE", StrikeOffset =    0 },
                    new LegFormula { Action = "SELL", Type = "CE", StrikeOffset = +200 },
                }
            },
            ["Bull Put Spread"] = new StrategyTemplate
            {
                Name = "Bull Put Spread", Category = "Bullish",
                Legs = new[]
                {
                    new LegFormula { Action = "SELL", Type = "PE", StrikeOffset = -100 },
                    new LegFormula { Action = "BUY",  Type = "PE", StrikeOffset = -300 },
                }
            },
            ["Bull Condor"] = new StrategyTemplate
            {
                Name = "Bull Condor", Category = "Bullish",
                Legs = new[]
                {
                    new LegFormula { Action = "BUY",  Type = "CE", StrikeOffset =    0 },
                    new LegFormula { Action = "SELL", Type = "CE", StrikeOffset = +100 },
                    new LegFormula { Action = "SELL", Type = "CE", StrikeOffset = +200 },
                    new LegFormula { Action = "BUY",  Type = "CE", StrikeOffset = +300 },
                }
            },
            ["Bull Butterfly"] = new StrategyTemplate
            {
                Name = "Bull Butterfly", Category = "Bullish",
                Legs = new[]
                {
                    new LegFormula { Action = "BUY",  Type = "CE", StrikeOffset =    0 },
                    new LegFormula { Action = "SELL", Type = "CE", StrikeOffset = +100 },
                    new LegFormula { Action = "SELL", Type = "CE", StrikeOffset = +100 },
                    new LegFormula { Action = "BUY",  Type = "CE", StrikeOffset = +200 },
                }
            },
            // Range Forward = buy OTM call, sell OTM put (zero/low net premium)
            ["Range Forward"] = new StrategyTemplate
            {
                Name = "Range Forward", Category = "Bullish",
                Legs = new[]
                {
                    new LegFormula { Action = "BUY",  Type = "CE", StrikeOffset = +200 },
                    new LegFormula { Action = "SELL", Type = "PE", StrikeOffset = -200 },
                }
            },

            // ── Bearish ───────────────────────────────────────────────────────────

            ["Short Call"] = new StrategyTemplate
            {
                Name = "Short Call", Category = "Bearish",
                Legs = new[]
                {
                    new LegFormula { Action = "SELL", Type = "CE", StrikeOffset = +200 },
                }
            },
            ["Long Put"] = new StrategyTemplate
            {
                Name = "Long Put", Category = "Bearish",
                Legs = new[]
                {
                    new LegFormula { Action = "BUY",  Type = "PE", StrikeOffset =    0 },
                }
            },
            ["Bear Call Spread"] = new StrategyTemplate
            {
                Name = "Bear Call Spread", Category = "Bearish",
                Legs = new[]
                {
                    new LegFormula { Action = "SELL", Type = "CE", StrikeOffset =    0 },
                    new LegFormula { Action = "BUY",  Type = "CE", StrikeOffset = +200 },
                }
            },
            ["Bear Put Spread"] = new StrategyTemplate
            {
                Name = "Bear Put Spread", Category = "Bearish",
                Legs = new[]
                {
                    new LegFormula { Action = "BUY",  Type = "PE", StrikeOffset =    0 },
                    new LegFormula { Action = "SELL", Type = "PE", StrikeOffset = -200 },
                }
            },
            ["Bear Condor"] = new StrategyTemplate
            {
                Name = "Bear Condor", Category = "Bearish",
                Legs = new[]
                {
                    new LegFormula { Action = "SELL", Type = "PE", StrikeOffset =    0 },
                    new LegFormula { Action = "BUY",  Type = "PE", StrikeOffset = -100 },
                    new LegFormula { Action = "BUY",  Type = "PE", StrikeOffset = -200 },
                    new LegFormula { Action = "SELL", Type = "PE", StrikeOffset = -300 },
                }
            },
            ["Bear Butterfly"] = new StrategyTemplate
            {
                Name = "Bear Butterfly", Category = "Bearish",
                Legs = new[]
                {
                    new LegFormula { Action = "SELL", Type = "PE", StrikeOffset =    0 },
                    new LegFormula { Action = "BUY",  Type = "PE", StrikeOffset = -100 },
                    new LegFormula { Action = "BUY",  Type = "PE", StrikeOffset = -100 },
                    new LegFormula { Action = "SELL", Type = "PE", StrikeOffset = -200 },
                }
            },
            // Risk Reversal = buy OTM put, sell OTM call (bearish hedge)
            ["Risk Reversal"] = new StrategyTemplate
            {
                Name = "Risk Reversal", Category = "Bearish",
                Legs = new[]
                {
                    new LegFormula { Action = "BUY",  Type = "PE", StrikeOffset = -200 },
                    new LegFormula { Action = "SELL", Type = "CE", StrikeOffset = +200 },
                }
            },
        };

        public static StrategyTemplate? GetTemplate(string name)
        {
            _templates.TryGetValue(name, out var template);
            return template;
        }
    }
}
