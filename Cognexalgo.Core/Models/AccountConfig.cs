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
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }
        private string _status = "Active";

        [Column("description")]
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }
        private string _description;

        [Column("is_enabled")]
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }
        private bool _isEnabled = true;

        [Column("api_key")]
        public string ApiKey { get; set; }

        [Column("totp_key")]
        public string TotpKey { get; set; }

        // --- Real-time / Runtime Properties (Not Persisted) ---

        [NotMapped]
        public bool IsFeedActive
        {
            get => _isFeedActive;
            set => SetProperty(ref _isFeedActive, value);
        }
        private bool _isFeedActive;

        [NotMapped]
        public string FeedStatusColor
        {
            get => _feedStatusColor;
            set => SetProperty(ref _feedStatusColor, value);
        }
        private string _feedStatusColor = "Gray";

        [NotMapped]
        public decimal Pnl
        {
            get => _pnl;
            set => SetProperty(ref _pnl, value);
        }
        private decimal _pnl;

        [NotMapped]
        public decimal MtmHigh
        {
            get => _mtmHigh;
            set => SetProperty(ref _mtmHigh, value);
        }
        private decimal _mtmHigh;

        [NotMapped]
        public decimal MtmLow
        {
            get => _mtmLow;
            set => SetProperty(ref _mtmLow, value);
        }
        private decimal _mtmLow;

        [NotMapped]
        public decimal Multiplier
        {
            get => _multiplier;
            set => SetProperty(ref _multiplier, value);
        }
        private decimal _multiplier = 1.0m;

        // Funds Group
        [NotMapped]
        public decimal FundsUtilized
        {
            get => _fundsUtilized;
            set => SetProperty(ref _fundsUtilized, value);
        }
        private decimal _fundsUtilized;

        [NotMapped]
        public decimal FundsAvailable
        {
            get => _fundsAvailable;
            set => SetProperty(ref _fundsAvailable, value);
        }
        private decimal _fundsAvailable;

        // Position Group
        [NotMapped]
        public int PositionTotal
        {
            get => _positionTotal;
            set => SetProperty(ref _positionTotal, value);
        }
        private int _positionTotal;
        
        [NotMapped]
        public int PositionOpen
        {
            get => _positionOpen;
            set => SetProperty(ref _positionOpen, value);
        }
        private int _positionOpen;
        
        [NotMapped]
        public int PositionClosed
        {
            get => _positionClosed;
            set => SetProperty(ref _positionClosed, value);
        }
        private int _positionClosed;

        [NotMapped]
        public DateTime PositionLastSync
        {
            get => _positionLastSync;
            set => SetProperty(ref _positionLastSync, value);
        }
        private DateTime _positionLastSync;

        // Orders Group
        [NotMapped]
        public int OrderTotal
        {
            get => _orderTotal;
            set => SetProperty(ref _orderTotal, value);
        }
        private int _orderTotal;

        [NotMapped]
        public int OrderOpen
        {
            get => _orderOpen;
            set => SetProperty(ref _orderOpen, value);
        }
        private int _orderOpen;

        [NotMapped]
        public int OrderTrgPend
        {
            get => _orderTrgPend;
            set => SetProperty(ref _orderTrgPend, value);
        }
        private int _orderTrgPend;

        [NotMapped]
        public int OrderCompl
        {
            get => _orderCompl;
            set => SetProperty(ref _orderCompl, value);
        }
        private int _orderCompl;

        [NotMapped]
        public int OrderRej
        {
            get => _orderRej;
            set => SetProperty(ref _orderRej, value);
        }
        private int _orderRej;

        [NotMapped]
        public int OrderCancel
        {
            get => _orderCancel;
            set => SetProperty(ref _orderCancel, value);
        }
        private int _orderCancel;

        [NotMapped]
        public DateTime OrderLastSync
        {
            get => _orderLastSync;
            set => SetProperty(ref _orderLastSync, value);
        }
        private DateTime _orderLastSync;
    }
}
