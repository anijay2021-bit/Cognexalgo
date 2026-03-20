using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Cognexalgo.Core.Domain.Patterns;
using Cognexalgo.Core.Models;

namespace Cognexalgo.UI.Models
{
    public partial class VCPSignalRow : ObservableObject
    {
        [ObservableProperty] private string _symbol       = string.Empty;
        [ObservableProperty] private string _quality      = string.Empty;
        [ObservableProperty] private string _qualityColor = "#9E9E9E";
        [ObservableProperty] private decimal _pivotLevel;
        [ObservableProperty] private decimal _stopLoss;
        [ObservableProperty] private decimal _target1;
        [ObservableProperty] private decimal _target2;
        [ObservableProperty] private decimal _riskReward;
        [ObservableProperty] private string _strike       = string.Empty;
        [ObservableProperty] private string _expiry       = string.Empty;
        [ObservableProperty] private string _signalAge    = "0m ago";
        [ObservableProperty] private string _timeframe    = string.Empty;

        /// <summary>Stored for age calculation — not directly bound.</summary>
        public DateTime SignalTime { get; private set; }

        public static VCPSignalRow FromSignal(VCPSignal signal)
        {
            var quality = signal.Pattern.Quality;

            return new VCPSignalRow
            {
                Symbol       = signal.Pattern.Symbol,
                Quality      = quality.ToString(),
                QualityColor = quality switch
                {
                    VCPQuality.A => "#FFD700",
                    VCPQuality.B => "#C0C0C0",
                    _            => "#9E9E9E"
                },
                PivotLevel  = signal.Pattern.PivotLevel,
                StopLoss    = signal.StopLoss,
                Target1     = signal.Target1,
                Target2     = signal.Target2,
                RiskReward  = signal.RiskRewardRatio,
                Strike      = signal.SuggestedStrike,
                Expiry      = signal.SuggestedExpiry,
                SignalTime  = signal.SignalTime,
                Timeframe   = signal.Pattern.Timeframe,
                SignalAge   = "0m ago"
            };
        }
    }
}
