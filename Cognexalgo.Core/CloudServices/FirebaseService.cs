using System;
using System.Threading.Tasks;
using Cognexalgo.Core.Models;
using FireSharp;
using FireSharp.Config;
using FireSharp.Interfaces;
using FireSharp.Response;
using Newtonsoft.Json;

namespace Cognexalgo.Core.CloudServices
{
    public class FirebaseService : IFirebaseService
    {
        private IFirebaseClient? _client;

        public async Task<bool> ConnectAsync(string firebaseBasePath, string firebaseSecret)
        {
            try
            {
                IFirebaseConfig config = new FirebaseConfig
                {
                    AuthSecret = firebaseSecret,
                    BasePath = firebaseBasePath
                };

                _client = new FirebaseClient(config);
                return _client != null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Firebase Connection Error: {ex.Message}");
                return false;
            }
        }

        public async Task<LicenseInfo> ValidateLicenseAsync(string licenseKey)
        {
            if (_client == null || string.IsNullOrEmpty(licenseKey))
                return new LicenseInfo { IsActive = false, Message = "Cloud Service Not Connected or Invalid Key" };

            try
            {
                FirebaseResponse response = await _client.GetAsync($"licenses/{licenseKey}");
                if (response.Body == "null")
                {
                    return new LicenseInfo { IsActive = false, Message = "License Key Not Found" };
                }

                var license = response.ResultAs<LicenseInfo>();
                license.LicenseKey = licenseKey; // Ensure key is set

                // Perform Logic Checks
                if (!license.IsActive)
                {
                    license.Message = "License is Inactive.";
                    return license;
                }

                if (!license.FeesPaid)
                {
                    license.Message = "Subscription Fees Pending.";
                    license.IsActive = false; // Override active status
                    return license;
                }

                if (license.ExpiryDate < DateTime.UtcNow)
                {
                    license.Message = $"License Expired on {license.ExpiryDate:yyyy-MM-dd}.";
                    license.IsActive = false;
                    return license;
                }

                license.Message = "Valid";
                return license;
            }
            catch (Exception ex)
            {
                // In case of network error, we might want to fail safe or block.
                // For security, blocking is safer.
                return new LicenseInfo { IsActive = false, Message = $"Validation Error: {ex.Message}" };
            }
        }

        public async Task PushHeartbeatAsync(ClientHeartbeat heartbeat)
        {
            if (_client == null) return;
            try
            {
                // We use Set to overwrite the current state
                await _client.SetAsync($"clients/{heartbeat.ClientId}/heartbeat", heartbeat);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Heartbeat Failed: {ex.Message}");
            }
        }

        public async Task<CloudCommand?> GetLatestCommandAsync(string clientId)
        {
            if (_client == null) return null;
            try
            {
                // Fetch the command
                FirebaseResponse response = await _client.GetAsync($"clients/{clientId}/command");
                if (response.Body == "null") return null;

                var command = response.ResultAs<CloudCommand>();
                
                // If the command is old (e.g., > 1 minute), ignore it to prevent looping old commands
                if (command != null && (DateTime.UtcNow - command.Timestamp).TotalMinutes > 1)
                {
                     return null;
                }

                return command;
            }
            catch
            {
                return null;
            }
        }
    }
}
