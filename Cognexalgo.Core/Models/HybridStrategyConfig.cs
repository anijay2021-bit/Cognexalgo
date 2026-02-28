using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cognexalgo.Core.Models
{
    public partial class HybridStrategyConfig : ObservableObject
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public bool IsActive { get; set; } = true;

        public List<StrategyLeg> Legs { get; set; } = new List<StrategyLeg>();
        public string ProductType { get; set; } = "MIS";
        public string ExpiryType { get; set; } = "Weekly";
        
        // UI Helper Properties
        public string StrategyType { get; set; } = "Hybrid";
        public string InstrumentType => Legs.Count > 0 ? Legs[0].Index : "N/A";

        // Dashboard Properties (Observable for Real-time Binding)
        [ObservableProperty]
        private string _status = "RUNNING"; 

        [ObservableProperty]
        private decimal _pnl;

        [ObservableProperty]
        private string _reason;

        [ObservableProperty]
        private DateTime _entryTime;

        [ObservableProperty]
        private DateTime? _exitTime;

        [ObservableProperty]
        private decimal _ltp;
        
        // Optional settings
        public bool AutoExecute { get; set; } = true;
        public string CandleStartTime { get; set; } = "09:15";
        public int MaxProfitPercent { get; set; } = 0; // 0 = disabled
        public int MaxLossPercent { get; set; } = 0; // 0 = disabled

        // JSON Storage for Strategy-Specific Parameters (e.g., Calendar Times)
        public string Parameters { get; set; } = "{}";

        // ── Trading Mode ───────────────────────────────────────────────────────
        /// <summary>Per-strategy live/paper toggle. Default: Paper (safe).</summary>
        [ObservableProperty]
        private bool _isLiveMode = false;

        public string TradingModeDisplay => IsLiveMode ? "LIVE" : "PAPER";

        // ── V2 Orchestrator tracking ─────────────────────────────────────────
        /// <summary>V2 strategy ID returned by V2StrategyAdapter.SyncToV2Async() e.g. "STR-20260227-STR-001".</summary>
        [ObservableProperty]
        private string _v2Id = string.Empty;

        /// <summary>Live status fed by StrategyOrchestrator.OnStatusChanged — "Active", "Paused", "Error".</summary>
        [ObservableProperty]
        private string _v2Status = string.Empty;
    }
}
