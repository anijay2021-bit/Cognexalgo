namespace Cognexalgo.UI.Services
{
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
                "Long Call",
                "Bear Call Spread",
                "Bear Put Spread",
                "Long Calendar With Puts",
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
                "Long Calendar With Calls",
                "Bull Condor",
                "Bull Butterfly",
                "Call Ratio Spread",
                "Range Forward"
            },
            _ => new List<string>()
        };
    }
}
