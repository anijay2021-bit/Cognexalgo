namespace AlgoTrader.MarketData;

/// <summary>Black-Scholes option pricing and Greeks calculator for Indian markets.</summary>
public static class GreeksCalculator
{
    /// <summary>Default risk-free rate (RBI repo rate).</summary>
    public const double DefaultRiskFreeRate = 0.065;

    /// <summary>Calculate option delta.</summary>
    public static double CalculateDelta(double S, double K, double T, double r, double sigma, bool isCall)
    {
        if (T <= 0 || sigma <= 0) return isCall ? (S > K ? 1 : 0) : (S < K ? -1 : 0);
        var d1 = D1(S, K, T, r, sigma);
        return isCall ? NormCdf(d1) : NormCdf(d1) - 1;
    }

    /// <summary>Calculate option gamma.</summary>
    public static double CalculateGamma(double S, double K, double T, double r, double sigma)
    {
        if (T <= 0 || sigma <= 0 || S <= 0) return 0;
        var d1 = D1(S, K, T, r, sigma);
        return NormPdf(d1) / (S * sigma * Math.Sqrt(T));
    }

    /// <summary>Calculate option theta (per day).</summary>
    public static double CalculateTheta(double S, double K, double T, double r, double sigma, bool isCall)
    {
        if (T <= 0 || sigma <= 0) return 0;
        var d1 = D1(S, K, T, r, sigma);
        var d2 = d1 - sigma * Math.Sqrt(T);
        var term1 = -S * NormPdf(d1) * sigma / (2 * Math.Sqrt(T));
        if (isCall)
            return (term1 - r * K * Math.Exp(-r * T) * NormCdf(d2)) / 365.0;
        else
            return (term1 + r * K * Math.Exp(-r * T) * NormCdf(-d2)) / 365.0;
    }

    /// <summary>Calculate option vega (per 1% move in IV).</summary>
    public static double CalculateVega(double S, double K, double T, double r, double sigma)
    {
        if (T <= 0 || sigma <= 0) return 0;
        var d1 = D1(S, K, T, r, sigma);
        return S * Math.Sqrt(T) * NormPdf(d1) / 100.0;
    }

    /// <summary>Calculate Black-Scholes option price.</summary>
    public static double CalculatePrice(double S, double K, double T, double r, double sigma, bool isCall)
    {
        if (T <= 0) return Math.Max(isCall ? S - K : K - S, 0);
        if (sigma <= 0) return Math.Max(isCall ? S - K : K - S, 0) * Math.Exp(-r * T);

        var d1 = D1(S, K, T, r, sigma);
        var d2 = d1 - sigma * Math.Sqrt(T);

        if (isCall)
            return S * NormCdf(d1) - K * Math.Exp(-r * T) * NormCdf(d2);
        else
            return K * Math.Exp(-r * T) * NormCdf(-d2) - S * NormCdf(-d1);
    }

    /// <summary>Calculate implied volatility using Newton-Raphson method.</summary>
    public static double CalculateIV(double marketPrice, double S, double K, double T, double r, bool isCall,
        double initialGuess = 0.2, int maxIterations = 100, double tolerance = 1e-6)
    {
        if (T <= 0 || marketPrice <= 0) return 0;

        double sigma = initialGuess;

        for (int i = 0; i < maxIterations; i++)
        {
            var price = CalculatePrice(S, K, T, r, sigma, isCall);
            var vega = CalculateVega(S, K, T, r, sigma) * 100; // undo the /100

            if (Math.Abs(vega) < 1e-12) break;

            var diff = price - marketPrice;
            if (Math.Abs(diff) < tolerance) return sigma;

            sigma -= diff / vega;

            // Clamp to reasonable range
            if (sigma < 0.001) sigma = 0.001;
            if (sigma > 5.0) sigma = 5.0;
        }

        return sigma;
    }

    /// <summary>Time to expiry in years for Indian markets (trading days basis).</summary>
    public static double TimeToExpiry(DateTime expiry)
    {
        var totalDays = (expiry.Date.AddHours(15.5) - DateTime.Now).TotalDays; // 3:30 PM expiry
        return Math.Max(totalDays / 365.0, 0.0001); // Prevent zero
    }

    // ─── Internals ───
    private static double D1(double S, double K, double T, double r, double sigma)
        => (Math.Log(S / K) + (r + 0.5 * sigma * sigma) * T) / (sigma * Math.Sqrt(T));

    /// <summary>Standard normal CDF (Abramowitz & Stegun approximation).</summary>
    private static double NormCdf(double x)
    {
        const double a1 = 0.254829592, a2 = -0.284496736, a3 = 1.421413741;
        const double a4 = -1.453152027, a5 = 1.061405429, p = 0.3275911;

        int sign = x < 0 ? -1 : 1;
        x = Math.Abs(x) / Math.Sqrt(2);
        double t = 1.0 / (1.0 + p * x);
        double y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);
        return 0.5 * (1.0 + sign * y);
    }

    /// <summary>Standard normal PDF.</summary>
    private static double NormPdf(double x)
        => Math.Exp(-0.5 * x * x) / Math.Sqrt(2 * Math.PI);
}
