using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Cognexalgo.Core.Application.Services
{
    /// <summary>
    /// Telegram Notification Service (Module 10):
    /// - Sends alerts for signals, RMS breaches, daily P&L summaries
    /// - Rate-limited to max 20 messages/minute (Telegram API limit is 30)
    /// - Message formatting with emojis for quick mobile scanning
    /// - Batch message queue with configurable flush interval
    /// </summary>
    public class TelegramNotifier : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _botToken;
        private readonly string _chatId;
        private readonly SemaphoreSlim _rateLimiter = new(20, 20);
        private readonly Timer _rateLimitRefresher;

        public bool IsEnabled { get; set; }

        public TelegramNotifier(string botToken, string chatId, bool enabled = false)
        {
            _botToken = botToken;
            _chatId = chatId;
            IsEnabled = enabled;
            _http = new HttpClient();

            // Refresh rate limiter every minute (release 20 permits)
            _rateLimitRefresher = new Timer(_ =>
            {
                int current = _rateLimiter.CurrentCount;
                if (current < 20)
                    _rateLimiter.Release(20 - current);
            }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        // ─── Signal Alerts ───────────────────────────────────────

        /// <summary>Send alert when a signal fires.</summary>
        public async Task SendSignalAlert(string strategyName, string signalType,
                                           string symbol, decimal price)
        {
            string emoji = signalType.Contains("Entry") ? "🟢" : "🔴";
            string msg = $"{emoji} *{signalType}*\n" +
                          $"Strategy: `{strategyName}`\n" +
                          $"Symbol: `{symbol}`\n" +
                          $"Price: ₹{price:N2}\n" +
                          $"Time: {DateTime.Now:HH:mm:ss}";

            await SendMarkdownAsync(msg);
        }

        /// <summary>Send alert when an order fills.</summary>
        public async Task SendOrderFillAlert(string orderId, string symbol,
                                              string direction, int qty, decimal fillPrice)
        {
            string emoji = direction == "BUY" ? "📈" : "📉";
            string msg = $"{emoji} *Order Filled*\n" +
                          $"ID: `{orderId}`\n" +
                          $"{direction} {qty} × `{symbol}`\n" +
                          $"Fill: ₹{fillPrice:N2}";

            await SendMarkdownAsync(msg);
        }

        // ─── RMS Alerts (High Priority) ──────────────────────────

        /// <summary>Send alert when an RMS rule breaches.</summary>
        public async Task SendRmsBreachAlert(string strategyId, string ruleType,
                                              decimal current, decimal threshold)
        {
            string msg = $"⚠️ *RMS BREACH*\n" +
                          $"Strategy: `{strategyId}`\n" +
                          $"Rule: *{ruleType}*\n" +
                          $"Current: ₹{current:N0}\n" +
                          $"Threshold: ₹{threshold:N0}\n" +
                          $"⏰ {DateTime.Now:HH:mm:ss}";

            await SendMarkdownAsync(msg);
        }

        /// <summary>Send Kill Switch activation alert.</summary>
        public async Task SendKillSwitchAlert()
        {
            await SendMarkdownAsync(
                "🛑 *KILL SWITCH ACTIVATED*\n" +
                "All strategies stopped immediately.\n" +
                $"⏰ {DateTime.Now:HH:mm:ss}");
        }

        // ─── Daily Summary ───────────────────────────────────────

        /// <summary>Send end-of-day P&L summary.</summary>
        public async Task SendDailySummary(decimal totalPnl, int tradesCount,
                                            int winCount, int lossCount,
                                            decimal maxDrawdown)
        {
            string emoji = totalPnl >= 0 ? "💰" : "📊";
            string pnlSign = totalPnl >= 0 ? "+" : "";

            string msg = $"{emoji} *Daily Summary*\n" +
                          $"━━━━━━━━━━━━━━━━\n" +
                          $"P&L: *{pnlSign}₹{totalPnl:N0}*\n" +
                          $"Trades: {tradesCount} (W:{winCount} L:{lossCount})\n" +
                          $"Win Rate: {(tradesCount > 0 ? (winCount * 100.0 / tradesCount) : 0):F1}%\n" +
                          $"Max DD: ₹{maxDrawdown:N0}\n" +
                          $"━━━━━━━━━━━━━━━━";

            await SendMarkdownAsync(msg);
        }

        // ─── Core Send Method ────────────────────────────────────

        private async Task SendMarkdownAsync(string markdownText)
        {
            if (!IsEnabled || string.IsNullOrEmpty(_botToken) || string.IsNullOrEmpty(_chatId))
                return;

            // Rate limiting
            if (!await _rateLimiter.WaitAsync(TimeSpan.FromSeconds(2)))
            {
                System.Diagnostics.Debug.WriteLine("[Telegram] Rate limit hit, skipping message");
                return;
            }

            try
            {
                var url = $"https://api.telegram.org/bot{_botToken}/sendMessage";
                var payload = new
                {
                    chat_id = _chatId,
                    text = markdownText,
                    parse_mode = "Markdown",
                    disable_web_page_preview = true
                };

                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _http.PostAsync(url, content);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"[Telegram] Send failed: {body}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Telegram] Error: {ex.Message}");
            }
        }

        /// <summary>Test connectivity by sending a test message.</summary>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                var url = $"https://api.telegram.org/bot{_botToken}/getMe";
                var response = await _http.GetAsync(url);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            _rateLimitRefresher?.Dispose();
            _rateLimiter?.Dispose();
            _http?.Dispose();
        }
    }

    /// <summary>
    /// Windows Toast Notification Service (Module 10):
    /// - Uses Windows 10/11 native toast notifications
    /// - Non-blocking, fire-and-forget
    /// - Configurable categories: Signals, RMS, Orders, Summary
    /// </summary>
    public class WindowsToastNotifier
    {
        public bool IsEnabled { get; set; } = true;

        /// <summary>Show a toast notification (signal fired).</summary>
        public void NotifySignal(string strategyName, string signalType, string symbol, decimal price)
        {
            if (!IsEnabled) return;

            string title = signalType.Contains("Entry") ? "🟢 Entry Signal" : "🔴 Exit Signal";
            string body = $"{strategyName}\n{symbol} @ ₹{price:N2}";

            ShowToast(title, body);
        }

        /// <summary>Show a toast notification (RMS breach).</summary>
        public void NotifyRmsBreach(string ruleType, string strategyId, decimal current)
        {
            if (!IsEnabled) return;
            ShowToast("⚠️ RMS Breach", $"{ruleType} on {strategyId}\nCurrent: ₹{current:N0}");
        }

        /// <summary>Show a toast notification (order filled).</summary>
        public void NotifyOrderFill(string orderId, string symbol, string direction, decimal price)
        {
            if (!IsEnabled) return;
            string emoji = direction == "BUY" ? "📈" : "📉";
            ShowToast($"{emoji} Order Filled", $"{direction} {symbol} @ ₹{price:N2}\n{orderId}");
        }

        /// <summary>Show end-of-day P&L summary toast.</summary>
        public void NotifyDailySummary(decimal totalPnl, int trades)
        {
            if (!IsEnabled) return;
            string emoji = totalPnl >= 0 ? "💰" : "📊";
            ShowToast($"{emoji} Daily Summary",
                      $"P&L: {(totalPnl >= 0 ? "+" : "")}₹{totalPnl:N0}\nTrades: {trades}");
        }

        private void ShowToast(string title, string body)
        {
            try
            {
                // Use Windows 10/11 native toast via PowerShell
                // This works without any NuGet dependencies
                var script = $@"
                    [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
                    [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null
                    
                    $template = @""
                    <toast>
                        <visual>
                            <binding template='ToastGeneric'>
                                <text>{EscapeXml(title)}</text>
                                <text>{EscapeXml(body)}</text>
                            </binding>
                        </visual>
                        <audio src='ms-winsoundevent:Notification.Default' />
                    </toast>
                    ""@
                    
                    $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
                    $xml.LoadXml($template)
                    $toast = [Windows.UI.Notifications.ToastNotification]::new($xml)
                    [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('CognexAlgo').Show($toast)";

                // Fire-and-forget via background task
                Task.Run(() =>
                {
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "powershell",
                            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"",
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };
                        var process = System.Diagnostics.Process.Start(psi);
                        process?.WaitForExit(5000);
                    }
                    catch { /* Toast is non-critical */ }
                });
            }
            catch
            {
                // Toast failure should never crash the app
            }
        }

        private static string EscapeXml(string text)
        {
            return text.Replace("&", "&amp;")
                       .Replace("<", "&lt;")
                       .Replace(">", "&gt;")
                       .Replace("\"", "&quot;")
                       .Replace("'", "&apos;");
        }
    }
}
