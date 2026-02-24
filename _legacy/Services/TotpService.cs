using System;
using OtpNet;

namespace Cognexalgo.Core.Services
{
    public class TotpService
    {
        public string GenerateTotp(string secretKey)
        {
            try 
            {
                if (string.IsNullOrWhiteSpace(secretKey)) return null;

                // Clean key (remove spaces if any)
                secretKey = secretKey.Replace(" ", "").Trim();

                var bytes = Base32Encoding.ToBytes(secretKey);
                var totp = new Totp(bytes);
                return totp.ComputeTotp();
            }
            catch (Exception ex)
            {
                // Key might be invalid
                Console.WriteLine($"Error generating TOTP: {ex.Message}");
                return null;
            }
        }
    }
}
