using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TempQuery
{
    /// <summary>
    /// Telegram Bot Setup Helper:
    /// 1. Run with --setup to test bot token
    /// 2. Run with --chatid to get your Chat ID 
    /// 3. Run with --test to send a test alert
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== CognexAlgo Telegram Setup ===\n");
            Console.WriteLine("Steps to configure Telegram alerts:\n");
            Console.WriteLine("1. Open Telegram → search @BotFather");
            Console.WriteLine("2. Send /newbot → follow prompts → copy the BOT TOKEN");
            Console.WriteLine("3. Start a chat with your new bot");
            Console.WriteLine("4. Run this tool with your token to get Chat ID\n");

            Console.Write("Enter Bot Token (or 'skip' to skip): ");
            string token = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrEmpty(token) || token == "skip")
            {
                Console.WriteLine("\nSkipped Telegram setup. You can configure later in appsettings.json.");
                return;
            }

            var http = new HttpClient();

            // Step 1: Test bot token
            Console.WriteLine("\n[1/3] Testing bot token...");
            try
            {
                var resp = await http.GetStringAsync($"https://api.telegram.org/bot{token}/getMe");
                var data = JObject.Parse(resp);
                if (data["ok"]?.Value<bool>() == true)
                {
                    string botName = data["result"]?["username"]?.ToString() ?? "Unknown";
                    Console.WriteLine($"[OK] Bot verified: @{botName}");
                }
                else
                {
                    Console.WriteLine("[ERROR] Invalid bot token!");
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] {ex.Message}");
                return;
            }

            // Step 2: Get Chat ID
            Console.WriteLine("\n[2/3] Getting Chat ID (make sure you've sent a message to your bot first)...");
            try
            {
                var resp = await http.GetStringAsync($"https://api.telegram.org/bot{token}/getUpdates");
                var data = JObject.Parse(resp);
                var updates = data["result"] as JArray;
                
                if (updates == null || updates.Count == 0)
                {
                    Console.WriteLine("[WARN] No messages found. Please:");
                    Console.WriteLine("  1. Open Telegram and find your bot");
                    Console.WriteLine("  2. Send it any message (e.g. 'hello')");
                    Console.WriteLine("  3. Run this tool again");
                    return;
                }

                string chatId = updates[0]["message"]?["chat"]?["id"]?.ToString() ?? "";
                string firstName = updates[0]["message"]?["chat"]?["first_name"]?.ToString() ?? "User";
                Console.WriteLine($"[OK] Chat ID: {chatId} (User: {firstName})");

                // Step 3: Send test message
                Console.WriteLine("\n[3/3] Sending test notification...");
                var payload = new
                {
                    chat_id = chatId,
                    text = "🟢 *CognexAlgo V2 Connected!*\n\nTelegram alerts are now active.\n\nYou'll receive:\n• Signal alerts 📈\n• Order fills 📊\n• RMS breaches ⚠️\n• Daily summaries 💰",
                    parse_mode = "Markdown"
                };

                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                var sendResp = await http.PostAsync($"https://api.telegram.org/bot{token}/sendMessage", content);
                
                if (sendResp.IsSuccessStatusCode)
                {
                    Console.WriteLine("[OK] Test message sent! Check your Telegram.");
                    Console.WriteLine($"\n=== Add these to appsettings.json ===");
                    Console.WriteLine($"\"TelegramEnabled\": true,");
                    Console.WriteLine($"\"TelegramBotToken\": \"{token}\",");
                    Console.WriteLine($"\"TelegramChatId\": \"{chatId}\"");
                }
                else
                {
                    Console.WriteLine($"[ERROR] Send failed: {await sendResp.Content.ReadAsStringAsync()}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] {ex.Message}");
            }
        }
    }
}
