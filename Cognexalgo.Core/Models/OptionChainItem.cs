using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Cognexalgo.Core.Models
{
    /// <summary>
    /// Represents a single option in the option chain with real-time data.
    /// Live-updating properties (LTP, IV, Greeks) implement INotifyPropertyChanged
    /// so WPF bindings refresh when SmartStream ticks arrive.
    /// </summary>
    public class OptionChainItem : INotifyPropertyChanged  // COGNEX-CHANGE: implement INPC for live fields
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (!Equals(field, value))
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }

        // ── Static properties — set once at chain load, no INPC needed ───────

        /// <summary>Strike price (e.g., 23500, 23550, 23600)</summary>
        public int Strike { get; set; }

        /// <summary>Option type: "CE" (Call) or "PE" (Put)</summary>
        public string OptionType { get; set; }

        /// <summary>Trading symbol (e.g., "NIFTY13FEB2624500CE")</summary>
        public string Symbol { get; set; }

        /// <summary>Angel One symbol token</summary>
        public string Token { get; set; }

        /// <summary>Lot size for this instrument</summary>
        public int LotSize { get; set; }

        /// <summary>Checks if this is a Call option</summary>
        public bool IsCall => OptionType == "CE";

        /// <summary>Checks if this is a Put option</summary>
        public bool IsPut => OptionType == "PE";

        // Expiry tracking — populated by BuildOptionChainAsync
        public DateTime ExpiryDate { get; set; }

        /// <summary>Calendar days to expiry (min 1 to avoid div-by-zero in Black-Scholes).</summary>
        public int DaysToExpiry => Math.Max(1, (ExpiryDate.Date - DateTime.Today).Days);

        // ── Live-updating properties — raise PropertyChanged so UI refreshes ─

        private double _ltp;
        /// <summary>Last Traded Price (real-time premium)</summary>
        public double LTP { get => _ltp; set => Set(ref _ltp, value); }  // COGNEX-CHANGE: INPC

        private double _iv;
        public double IV    { get => _iv;    set => Set(ref _iv, value);    }  // COGNEX-CHANGE: INPC

        private double _delta;
        public double Delta { get => _delta; set => Set(ref _delta, value); }  // COGNEX-CHANGE: INPC

        private double _theta;
        public double Theta { get => _theta; set => Set(ref _theta, value); }  // COGNEX-CHANGE: INPC

        private double _vega;
        public double Vega  { get => _vega;  set => Set(ref _vega, value);  }  // COGNEX-CHANGE: INPC

        private double _gamma;
        public double Gamma { get => _gamma; set => Set(ref _gamma, value); }  // COGNEX-CHANGE: INPC

        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Alias for Symbol — used by CalendarStrategy / shared callers.</summary>
        public string TradingSymbol => Symbol ?? "";

        /// <summary>
        /// True when this is a weekly (non-monthly) expiry.
        /// Heuristic: if the Thursday one week later falls in the same month, this is NOT the
        /// last Thursday → weekly.  If it rolls into the next month, this IS the monthly expiry.
        /// </summary>
        public bool IsWeeklyExpiry
        {
            get
            {
                if (ExpiryDate == default) return DaysToExpiry <= 10;
                // The next Thursday after this expiry
                return ExpiryDate.AddDays(7).Month == ExpiryDate.Month;
            }
        }

        public override string ToString()
        {
            return $"{Strike} {OptionType} @ ₹{LTP:N2}";
        }
    }
}
