using System;

namespace Cognexalgo.Core.Models
{
    public class LicenseInfo
    {
        public string LicenseKey { get; set; }
        public bool IsActive { get; set; }
        public bool FeesPaid { get; set; }
        public DateTime ExpiryDate { get; set; }
        public string OwnerName { get; set; }
        public string Message { get; set; } // "Subscription Expired", "Payment Pending", etc.
    }

    public class ClientHeartbeat
    {
        public string ClientId { get; set; }
        public string MachineName { get; set; }
        public string Version { get; set; }
        public string Status { get; set; } // "RUNNING", "STOPPED", "ERROR"
        public decimal TotalPnL { get; set; }
        public int ActiveStrategiesCount { get; set; }
        public int OpenPositionsCount { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public class CloudCommand
    {
        public string CommandId { get; set; }
        public string Action { get; set; } // "STOP", "SQUARE_OFF", "MESSAGE"
        public string Payload { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
