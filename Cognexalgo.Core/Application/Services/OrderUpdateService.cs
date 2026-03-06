using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Cognexalgo.Core.Application.Services
{
    /// <summary>
    /// Connects to Angel One's dedicated order update WebSocket:
    ///   wss://tns.angelone.in/smart-order-update
    ///
    /// No subscription payload is required — the server automatically streams all order
    /// updates for the authenticated account once the Bearer token is validated.
    ///
    /// Angel One status codes:
    ///   AB00 = Connected, AB01 = Open, AB02 = Cancelled, AB03 = Rejected,
    ///   AB04 = Modified, AB05 = Complete, AB06 = AMO received,
    ///   AB09 = Open Pending, AB10 = Trigger Pending
    /// </summary>
    public class OrderUpdateService : IDisposable
    {
        private const string WsUrl = "wss://tns.angelone.in/smart-order-update";

        private ClientWebSocket? _ws;
        private CancellationTokenSource? _cts;
        private Timer? _pingTimer;

        /// <summary>
        /// Fired when a terminal order status update is received.
        /// Parameters: (brokerOrderId, statusCode, filledPrice, filledQty)
        /// Consumers should act on AB05 (complete), AB03 (rejected), AB02 (cancelled).
        /// </summary>
        public event Action<string, string, decimal, int>? OnOrderUpdate;
        public event Action<string, string>? OnLog;

        public bool IsConnected => _ws?.State == WebSocketState.Open;

        /// <summary>
        /// Connect to the order update stream using the session JWT token.
        /// Call this once after successful V1 ConnectAsync.
        /// </summary>
        public async Task ConnectAsync(string jwtToken)
        {
            if (IsConnected) return;
            if (string.IsNullOrWhiteSpace(jwtToken)) return;

            _cts = new CancellationTokenSource();
            _ws = new ClientWebSocket();
            _ws.Options.SetRequestHeader("Authorization", $"Bearer {jwtToken}");

            try
            {
                await _ws.ConnectAsync(new Uri(WsUrl), _cts.Token);
                Log("INFO", "[OrderWS] Connected to Angel One order update stream");

                // Angel One requires a ping every 10 seconds to keep the connection alive
                _pingTimer = new Timer(async _ => await SendPingAsync(), null,
                    TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

                // Run receive loop in background — does not block the caller
                _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
            }
            catch (Exception ex)
            {
                Log("ERROR", $"[OrderWS] Connection failed: {ex.Message}");
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            var buffer = new byte[4096];
            var sb = new StringBuilder();

            while (_ws?.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                try
                {
                    sb.Clear();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close) break;

                    string json = sb.ToString().Trim();
                    if (!string.IsNullOrEmpty(json) && json != "pong")
                        ParseMessage(json);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log("ERROR", $"[OrderWS] Receive error: {ex.Message}");
                    break;
                }
            }

            Log("WARN", "[OrderWS] Receive loop ended — connection closed");
        }

        private void ParseMessage(string json)
        {
            try
            {
                var obj = JObject.Parse(json);
                string statusCode = obj["order-status"]?.ToString() ?? "";

                // AB00 = connection acknowledged — not an order update
                if (statusCode == "AB00")
                {
                    Log("INFO", "[OrderWS] Connection acknowledged (AB00)");
                    return;
                }

                var data = obj["orderData"];
                if (data == null) return;

                string brokerOrderId = data["orderid"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(brokerOrderId)) return;

                decimal filledPrice = decimal.TryParse(
                    data["averageprice"]?.ToString(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var p) ? p : 0m;

                int filledQty = int.TryParse(data["filledshares"]?.ToString(), out var q) ? q : 0;

                Log("INFO", $"[OrderWS] Update: {brokerOrderId} → {statusCode} @ ₹{filledPrice}");
                OnOrderUpdate?.Invoke(brokerOrderId, statusCode, filledPrice, filledQty);
            }
            catch (Exception ex)
            {
                Log("ERROR", $"[OrderWS] Parse error: {ex.Message}");
            }
        }

        private async Task SendPingAsync()
        {
            if (_ws?.State != WebSocketState.Open) return;
            try
            {
                var bytes = Encoding.UTF8.GetBytes("ping");
                await _ws.SendAsync(new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch { /* swallow — connection may have dropped, next ping will also fail and loop will end */ }
        }

        public void Disconnect()
        {
            _pingTimer?.Dispose();
            _pingTimer = null;
            _cts?.Cancel();
            try
            {
                _ws?.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutdown",
                    CancellationToken.None).Wait(1000);
            }
            catch { }
            _ws?.Dispose();
            _ws = null;
        }

        public void Dispose() => Disconnect();

        private void Log(string level, string msg) => OnLog?.Invoke(level, msg);
    }
}
