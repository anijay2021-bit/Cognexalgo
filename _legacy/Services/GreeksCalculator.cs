using System;

namespace Cognexalgo.Core.Services
{
    public class GreeksCalculator
    {
        // Black-Scholes Model for Options Greeks
        // S = Underlying Price, K = Strike Price, T = Time to Expiry (years), r = Risk-free rate, sigma = Volatility
        
        public static double CalculateDelta(double S, double K, double T, double r, double sigma, bool isCall)
        {
            if (T <= 0) return isCall ? (S >= K ? 1.0 : 0.0) : (S <= K ? -1.0 : 0.0);
            
            double d1 = (Math.Log(S / K) + (r + sigma * sigma / 2) * T) / (sigma * Math.Sqrt(T));
            double delta = Phi(d1);
            
            return isCall ? delta : delta - 1.0;
        }

        public static double CalculateTheta(double S, double K, double T, double r, double sigma, bool isCall)
        {
            if (T <= 0) return 0;

            double d1 = (Math.Log(S / K) + (r + sigma * sigma / 2) * T) / (sigma * Math.Sqrt(T));
            double d2 = d1 - sigma * Math.Sqrt(T);

            double part1 = -(S * sigma * Math.Exp(-0.5 * d1 * d1) / (Math.Sqrt(2 * Math.PI) * 2 * Math.Sqrt(T)));
            double part2 = r * K * Math.Exp(-r * T) * Phi(d2);

            if (isCall)
                return (part1 - part2) / 365.0; // Per day
            else
                return (part1 + r * K * Math.Exp(-r * T) * Phi(-d2)) / 365.0;
        }

        public static double CalculateVega(double S, double K, double T, double r, double sigma)
        {
            if (T <= 0) return 0;

            double d1 = (Math.Log(S / K) + (r + sigma * sigma / 2) * T) / (sigma * Math.Sqrt(T));
            return (S * Math.Sqrt(T) * Math.Exp(-0.5 * d1 * d1) / Math.Sqrt(2 * Math.PI)) / 100.0; // 1% change
        }

        // Standard Normal Cumulative Distribution Function
        private static double Phi(double x)
        {
            double a1 = 0.319381530;
            double a2 = -0.356563782;
            double a3 = 1.781477937;
            double a4 = -1.821255978;
            double a5 = 1.330274429;
            double L = Math.Abs(x);
            double K = 1.0 / (1.0 + 0.2316419 * L);
            double d = 0.39894228 * Math.Exp(-x * x / 2.0);
            double p = 1.0 - d * (a1 * K + a2 * K * K + a3 * Math.Pow(K, 3) + a4 * Math.Pow(K, 4) + a5 * Math.Pow(K, 5));
            return x >= 0 ? p : 1.0 - p;
        }
    }
}
