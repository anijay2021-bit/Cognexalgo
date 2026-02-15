using System;
using System.Threading.Tasks;
using Cognexalgo.Core.Models;

namespace Cognexalgo.Core.CloudServices
{
    public interface IFirebaseService
    {
        Task<bool> ConnectAsync(string firebaseBasePath, string firebaseSecret);
        Task<LicenseInfo> ValidateLicenseAsync(string licenseKey);
        Task PushHeartbeatAsync(ClientHeartbeat heartbeat);
        Task<CloudCommand?> GetLatestCommandAsync(string clientId);
    }
}
