using System;
using MathNet.Numerics.Distributions;

namespace Cognexalgo.Core.Services
{
    public class GreeksService
    {
        private const double DaysPerYear = 365.25;

        /// <summary>
        /// Calculates Option Greeks using Black-Scholes Model
        /// </summary>
        /// <param name="spotPrice">Current Underlying Price</param>
        /// <param name="strikePrice">Option Strike Price</param>
        /// <param name="timeToExpiryDays">Days until Expiry</param>
        /// <param name="riskFreeRate">Risk Free Interest Rate (e.g., 0.10 for 10%)</param>
        /// <param name="volatility">Implied Volatility (e.g., 0.20 for 20%)</param>
        /// <param name="isCall">True for Call, False for Put</param>
        public GreeksResult CalculateGreeks(double spotPrice, double strikePrice, double timeToExpiryDays, double riskFreeRate, double volatility, bool isCall)
        {
            if (timeToExpiryDays <= 0) timeToExpiryDays = 0.00001; // Avoid division by zero
            double t = timeToExpiryDays / DaysPerYear;
            double sqrtT = Math.Sqrt(t);

            double d1 = (Math.Log(spotPrice / strikePrice) + (riskFreeRate + (volatility * volatility) / 2.0) * t) / (volatility * sqrtT);
            double d2 = d1 - (volatility * sqrtT);

            double nd1 = Normal.CDF(0, 1, d1);
            double nd2 = Normal.CDF(0, 1, d2);
            double pdfd1 = Normal.PDF(0, 1, d1);

            var result = new GreeksResult();

            if (isCall)
            {
                result.Delta = nd1;
                result.Theta = (-(spotPrice * pdfd1 * volatility) / (2 * sqrtT) - riskFreeRate * strikePrice * Math.Exp(-riskFreeRate * t) * nd2) / DaysPerYear;
                result.Rho = (strikePrice * t * Math.Exp(-riskFreeRate * t) * nd2) / 100; // Scaled
            }
            else // Put
            {
                result.Delta = nd1 - 1;
                result.Theta = (-(spotPrice * pdfd1 * volatility) / (2 * sqrtT) + riskFreeRate * strikePrice * Math.Exp(-riskFreeRate * t) * (1 - nd2)) / DaysPerYear;
                result.Rho = (-strikePrice * t * Math.Exp(-riskFreeRate * t) * (1 - nd2)) / 100; // Scaled
            }

            result.Gamma = (pdfd1 / (spotPrice * volatility * sqrtT));
            result.Vega = (spotPrice * sqrtT * pdfd1) / 100; // Scaled for 1% IV change

            return result;
        }

        public double CalculateIV(double marketPrice, double spot, double strike, double daysToExpiry, double rate, bool isCall)
        {
            // Simple Bisection or Newton-Raphson could go here. 
            // For now, returning a placeholder or simplified estimation if needed.
            // Bootstrapper Requirement specified calculating Greeks, IV is usually an input or derived via solver.
            // We explain this limitation or implement a solver if requested.
            // Using a basic iterative solver for now.

            double low = 0.001;
            double high = 5.0;
            double epsilon = 0.001;
            int maxIterations = 100;

            for (int i = 0; i < maxIterations; i++)
            {
                double mid = (low + high) / 2.0;
                var greeks = CalculateGreeks(spot, strike, daysToExpiry, rate, mid, isCall);
                double modelPrice = CalculateOptionPrice(spot, strike, daysToExpiry, rate, mid, isCall);

                if (Math.Abs(modelPrice - marketPrice) < epsilon) return mid;
                if (modelPrice < marketPrice) low = mid;
                else high = mid;
            }

            return (low + high) / 2.0;
        }

        private double CalculateOptionPrice(double spot, double strike, double days, double rate, double vol, bool isCall)
        {
             double t = days / 365.25;
             double d1 = (Math.Log(spot / strike) + (rate + vol * vol / 2) * t) / (vol * Math.Sqrt(t));
             double d2 = d1 - vol * Math.Sqrt(t);

             if (isCall)
                 return spot * Normal.CDF(0, 1, d1) - strike * Math.Exp(-rate * t) * Normal.CDF(0, 1, d2);
             else
                 return strike * Math.Exp(-rate * t) * Normal.CDF(0, 1, -d2) - spot * Normal.CDF(0, 1, -d1);
        }
    }

    public class GreeksResult
    {
        public double Delta { get; set; }
        public double Gamma { get; set; }
        public double Theta { get; set; }
        public double Vega { get; set; }
        public double Rho { get; set; }
        public double IV { get; set; }
    }
}
