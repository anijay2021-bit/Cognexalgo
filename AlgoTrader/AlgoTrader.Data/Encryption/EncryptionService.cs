using System.Security.Cryptography;
using System.Text;
using AlgoTrader.Core.Interfaces;
using AlgoTrader.Core.Models;
using OtpNet;

namespace AlgoTrader.Data.Encryption;

/// <summary>AES-256-CBC encryption for credential storage.</summary>
public class AesEncryptionService : IEncryptionService
{
    private readonly byte[] _key;

    public AesEncryptionService()
    {
        // Derive a 256-bit key from machine-specific entropy
        var entropy = $"{Environment.MachineName}_AlgoTrader_SecretSalt_v1";
        using var sha = SHA256.Create();
        _key = sha.ComputeHash(Encoding.UTF8.GetBytes(entropy));
    }

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.Mode = CipherMode.CBC;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Prepend IV to ciphertext
        var result = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return string.Empty;

        var fullBytes = Convert.FromBase64String(cipherText);

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.Mode = CipherMode.CBC;

        // Extract IV from first 16 bytes
        var iv = new byte[16];
        Buffer.BlockCopy(fullBytes, 0, iv, 0, 16);
        aes.IV = iv;

        var cipherBytes = new byte[fullBytes.Length - 16];
        Buffer.BlockCopy(fullBytes, 16, cipherBytes, 0, cipherBytes.Length);

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }

    public string GenerateTOTP(string secret)
    {
        if (string.IsNullOrEmpty(secret)) return string.Empty;
        var key = Base32Encoding.ToBytes(secret);
        var totp = new Totp(key);
        return totp.ComputeTotp();
    }

    public bool ValidateTOTP(string secret, string totp)
    {
        if (string.IsNullOrEmpty(secret)) return false;
        var key = Base32Encoding.ToBytes(secret);
        var totpObj = new Totp(key);
        return totpObj.VerifyTotp(totp, out _, new VerificationWindow(1, 1)); // ±30 seconds
    }
}

/// <summary>Protects/unprotects sensitive fields in AccountCredential before DB storage.</summary>
public class CredentialProtector
{
    private readonly IEncryptionService _encryption;

    public CredentialProtector(IEncryptionService encryption)
    {
        _encryption = encryption;
    }

    /// <summary>Encrypts sensitive fields before saving to DB.</summary>
    public AccountCredential ProtectCredential(AccountCredential credential)
    {
        return new AccountCredential
        {
            ClientID = credential.ClientID,
            Password = _encryption.Encrypt(credential.Password),
            PIN = _encryption.Encrypt(credential.PIN),
            APIKey = credential.APIKey,
            APISecret = _encryption.Encrypt(credential.APISecret),
            TOTPSecret = _encryption.Encrypt(credential.TOTPSecret),
            BrokerType = credential.BrokerType,
            TokenExpiry = credential.TokenExpiry,
            AccountName = credential.AccountName,
            GroupName = credential.GroupName,
        };
    }

    /// <summary>Decrypts sensitive fields after loading from DB.</summary>
    public AccountCredential UnprotectCredential(AccountCredential credential)
    {
        return new AccountCredential
        {
            ClientID = credential.ClientID,
            Password = _encryption.Decrypt(credential.Password),
            PIN = _encryption.Decrypt(credential.PIN),
            APIKey = credential.APIKey,
            APISecret = _encryption.Decrypt(credential.APISecret),
            TOTPSecret = _encryption.Decrypt(credential.TOTPSecret),
            BrokerType = credential.BrokerType,
            TokenExpiry = credential.TokenExpiry,
            AccountName = credential.AccountName,
            GroupName = credential.GroupName,
        };
    }
}
