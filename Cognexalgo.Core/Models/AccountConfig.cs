using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Cognexalgo.Core.Models
{
    [Table("account_configs")]
    public partial class AccountConfig : ObservableObject
    {
        [Key]
        [Column("id")]
        public string ClientId { get; set; } // Key ID (e.g., A1234)

        [Column("account_name")]
        public string AccountName { get; set; }

        [Column("broker")]
        public string Broker { get; set; } = "Angel One";

        [Column("status")]
        [ObservableProperty]
        private string _status = "Active"; // Persisted

        [Column("description")]
        [ObservableProperty]
        private string _description;

        [Column("is_enabled")]
        [ObservableProperty]
        private bool _isEnabled = true;

        // --- Real-time / Runtime Properties (Not Persisted) ---

        [NotMapped]
        [ObservableProperty]
        private bool _isFeedActive;

        [NotMapped]
        [ObservableProperty]
        private string _feedStatusColor = "#Gray";

        [NotMapped]
        [ObservableProperty]
        private decimal _pnl;

        [NotMapped]
        [ObservableProperty]
        private decimal _mtmHigh;

        [NotMapped]
        [ObservableProperty]
        private decimal _mtmLow;

        [NotMapped]
        [ObservableProperty]
        private decimal _multiplier = 1.0m;

        // Funds Group
        [NotMapped]
        [ObservableProperty]
        private decimal _fundsUtilized;

        [NotMapped]
        [ObservableProperty]
        private decimal _fundsAvailable;

        // Position Group
        [NotMapped]
        [ObservableProperty]
        private int _positionTotal;
        
        [NotMapped]
        [ObservableProperty]
        private int _positionOpen;
        
        [NotMapped]
        [ObservableProperty]
        private int _positionClosed;

        [NotMapped]
        [ObservableProperty]
        private DateTime _positionLastSync;

        // Orders Group
        [NotMapped]
        [ObservableProperty]
        private int _orderTotal;

        [NotMapped]
        [ObservableProperty]
        private int _orderOpen;

        [NotMapped]
        [ObservableProperty]
        private int _orderTrgPend;

        [NotMapped]
        [ObservableProperty]
        private int _orderCompl;

        [NotMapped]
        [ObservableProperty]
        private int _orderRej;

        [NotMapped]
        [ObservableProperty]
        private int _orderCancel;

        [NotMapped]
        [ObservableProperty]
        private DateTime _orderLastSync;
    }
}
