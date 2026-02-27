using AlgoTrader.Core.Interfaces;
using AlgoTrader.Core.Models;
using Microsoft.Extensions.Logging;

namespace AlgoTrader.Brokers.AngelOne;

/// <summary>Handles automatic JWT token refresh before expiry.</summary>
public class AngelOneAuthService
{
    private readonly IBrokerFactory _brokerFactory;
    private readonly Action<AccountCredential> _persistCredential;
    private readonly ILogger<AngelOneAuthService> _logger;
    private readonly Dictionary<string, System.Threading.Timer> _refreshTimers = new();
    private readonly object _lock = new();

    public event EventHandler<string>? TokenRefreshed;
    public event EventHandler<(string AccountId, string Error)>? TokenRefreshFailed;

    public AngelOneAuthService(IBrokerFactory brokerFactory, Action<AccountCredential> persistCredential, ILogger<AngelOneAuthService> logger)
    {
        _brokerFactory = brokerFactory;
        _persistCredential = persistCredential;
        _logger = logger;
    }

    /// <summary>Schedules automatic token refresh 30 minutes before expiry.</summary>
    public void ScheduleTokenRefresh(AccountCredential credential)
    {
        lock (_lock)
        {
            // Cancel existing timer for this account
            if (_refreshTimers.TryGetValue(credential.ClientID, out var existingTimer))
            {
                existingTimer.Dispose();
                _refreshTimers.Remove(credential.ClientID);
            }

            if (credential.TokenExpiry == default || !credential.IsLoggedIn)
            {
                _logger.LogWarning("Cannot schedule refresh for {Client}: no expiry or not logged in", credential.ClientID);
                return;
            }

            // Calculate when to refresh: 30 min before expiry
            var refreshAt = credential.TokenExpiry - TimeSpan.FromMinutes(30);
            var delay = refreshAt - DateTime.UtcNow;

            if (delay <= TimeSpan.Zero)
            {
                // Already past refresh window — refresh immediately
                delay = TimeSpan.FromSeconds(5);
            }

            var timer = new System.Threading.Timer(
                async _ => await RefreshTokenCallbackAsync(credential),
                null,
                (int)delay.TotalMilliseconds,
                Timeout.Infinite // one-shot
            );

            _refreshTimers[credential.ClientID] = timer;
            _logger.LogInformation("Token refresh scheduled for {Client} in {Delay:N0}s (expiry: {Expiry:HH:mm:ss})",
                credential.ClientID, delay.TotalSeconds, credential.TokenExpiry);
        }
    }

    private async Task RefreshTokenCallbackAsync(AccountCredential credential)
    {
        const int maxRetries = 3;
        var retryDelays = new[] { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10) };

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                _logger.LogInformation("Refreshing token for {Client} (attempt {Attempt})", credential.ClientID, attempt + 1);

                var broker = _brokerFactory.Create(credential.BrokerType);
                var success = await broker.RefreshTokenAsync(credential);

                if (success)
                {
                    // Update credential in DB
                    _persistCredential(credential);
                    _logger.LogInformation("Token refreshed for {Client}, new expiry: {Expiry}",
                        credential.ClientID, credential.TokenExpiry);

                    TokenRefreshed?.Invoke(this, credential.ClientID);

                    // Schedule next refresh
                    ScheduleTokenRefresh(credential);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Token refresh failed for {Client} (attempt {Attempt})", credential.ClientID, attempt + 1);
            }

            if (attempt < maxRetries - 1)
            {
                _logger.LogWarning("Retrying token refresh in {Delay}", retryDelays[attempt]);
                await Task.Delay(retryDelays[attempt]);
            }
        }

        // All retries failed
        _logger.LogError("All token refresh attempts failed for {Client}", credential.ClientID);
        TokenRefreshFailed?.Invoke(this, (credential.ClientID, "All refresh attempts exhausted"));
    }

    /// <summary>Cancels all scheduled refreshes.</summary>
    public void CancelAll()
    {
        lock (_lock)
        {
            foreach (var timer in _refreshTimers.Values)
                timer.Dispose();
            _refreshTimers.Clear();
        }
    }
}
