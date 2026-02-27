using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using AlgoTrader.Core.Enums;
using AlgoTrader.Core.Interfaces;
using AlgoTrader.Core.Models;
using Microsoft.Extensions.Logging;

namespace AlgoTrader.Notify;

/// <summary>Telegram configuration.</summary>
public class TelegramConfig
{
    public string BotToken { get; set; } = string.Empty;
    public string DefaultChatId { get; set; } = string.Empty;
    public Dictionary<string, string> AccountChatIdMap { get; set; } = new();
    public bool IsEnabled { get; set; }
    public bool NotifyOnEntry { get; set; } = true;
    public bool NotifyOnExit { get; set; } = true;
    public bool NotifyOnSLHit { get; set; } = true;
    public bool NotifyOnTargetHit { get; set; } = true;
    public bool NotifyOnError { get; set; } = true;
    public int ThrottleSeconds { get; set; } = 60;
}

/// <summary>Telegram notification service for trade alerts.</summary>
public class TelegramNotificationService : INotificationService
{
    private readonly TelegramConfig _config;
    private readonly ILogger<TelegramNotificationService> _logger;
    private readonly ConcurrentQueue<(string ChatId, string Message)> _queue = new();
    private readonly HashSet<string> _recentMessages = new();
    private CancellationTokenSource? _cts;

    public TelegramNotificationService(TelegramConfig config, ILogger<TelegramNotificationService> logger)
    {
        _config = config;
        _logger = logger;

        if (_config.IsEnabled)
            StartQueueProcessor();
    }

    public async Task SendMessageAsync(string chatId, string message)
    {
        if (!_config.IsEnabled) return;

        // Throttle check
        var msgKey = $"{chatId}_{message.GetHashCode()}";
        lock (_recentMessages)
        {
            if (_recentMessages.Contains(msgKey)) return;
            _recentMessages.Add(msgKey);
        }

        _queue.Enqueue((chatId, message));
        _logger.LogDebug("Telegram message queued: {ChatId}", chatId);

        // Remove from throttle after configured seconds
        _ = Task.Delay(TimeSpan.FromSeconds(_config.ThrottleSeconds)).ContinueWith(_ =>
        {
            lock (_recentMessages) { _recentMessages.Remove(msgKey); }
        });
    }

    public async Task NotifyEntryAsync(StrategyConfig strategy, List<OrderBook> fills, AccountCredential account)
    {
        if (!_config.NotifyOnEntry) return;
        var fillSummary = string.Join(", ", fills.Select(f => $"{f.Symbol} {f.BuySell} {f.FilledQty}@{f.AvgPrice}"));
        var msg = $"✅ ENTRY | {strategy.Name} | {fillSummary} | {DateTime.Now:HH:mm:ss}";
        var chatId = GetChatId(account.ClientID);
        await SendMessageAsync(chatId, msg);
    }

    public async Task NotifyExitAsync(StrategyConfig strategy, ExitReason reason, decimal mtm, AccountCredential account)
    {
        if (!_config.NotifyOnExit) return;
        var emoji = mtm >= 0 ? "🟢" : "🔴";
        var msg = $"{emoji} EXIT | {strategy.Name} | Reason: {reason} | MTM: ₹{mtm:N2} | {DateTime.Now:HH:mm:ss}";
        var chatId = GetChatId(account.ClientID);
        await SendMessageAsync(chatId, msg);
    }

    public async Task NotifyErrorAsync(string context, Exception ex)
    {
        if (!_config.NotifyOnError) return;
        var msg = $"🚨 ERROR | {context} | {ex.Message}";
        await SendMessageAsync(_config.DefaultChatId, msg);
    }

    private string GetChatId(string accountId)
        => _config.AccountChatIdMap.GetValueOrDefault(accountId, _config.DefaultChatId);

    private void StartQueueProcessor()
    {
        _cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            using var httpClient = new HttpClient();
            while (!_cts.Token.IsCancellationRequested)
            {
                while (_queue.TryDequeue(out var item))
                {
                    try
                    {
                        var url = $"https://api.telegram.org/bot{_config.BotToken}/sendMessage";
                        var payload = new FormUrlEncodedContent(new[]
                        {
                            new KeyValuePair<string, string>("chat_id", item.ChatId),
                            new KeyValuePair<string, string>("text", item.Message),
                            new KeyValuePair<string, string>("parse_mode", "HTML"),
                        });
                        await httpClient.PostAsync(url, payload);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Telegram send failed");
                    }
                }
                await Task.Delay(500, _cts.Token);
            }
        });
    }
}

/// <summary>In-app alert event broadcasting.</summary>
public class InAppAlertService
{
    private readonly Subject<AlertEvent> _alertSubject = new();
    public IObservable<AlertEvent> Alerts => _alertSubject.AsObservable();

    public void RaiseAlert(AlertSeverity severity, string title, string message)
    {
        _alertSubject.OnNext(new AlertEvent(severity, title, message, DateTime.UtcNow));
    }
}
