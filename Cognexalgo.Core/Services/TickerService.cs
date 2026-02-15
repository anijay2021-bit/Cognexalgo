using System;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cognexalgo.Core.Models;
using Newtonsoft.Json;

namespace Cognexalgo.Core.Services
{
    public class TickerService
    {
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private readonly string _wsUrl; // Angel One URL
        
        // Events
        public event Action<TickerData> OnTickReceived;
        public event Action<string> OnStatusChanged;

        public bool IsConnected => _ws != null && _ws.State == WebSocketState.Open;

        public TickerService(string wsUrl)
        {
            _wsUrl = wsUrl;
        }

        public void UpdateTokens(string jwtToken, string feedToken)
        {
             // Append tokens to URL or Headers as required by Angel One
             // Angel One WebSocket URL format usually includes FeedToken:
             // wss://smartapi.angelbroking.com/websocket?jwttoken=...&&clientcode=...&apikey=...
             // For now, we'll store them, but real implementation needs the full URL construction logic.
        }

        // Public method to emit tick data (for simulated/mock data)
        public void EmitTick(TickerData data)
        {
            OnTickReceived?.Invoke(data);
        }

        public async Task ConnectAsync()
        {
            if (_ws != null && _ws.State == WebSocketState.Open) return;

            _cts = new CancellationTokenSource();
            _ws = new ClientWebSocket();

            try
            {
                OnStatusChanged?.Invoke("Connecting...");
                await _ws.ConnectAsync(new Uri(_wsUrl), _cts.Token);
                OnStatusChanged?.Invoke("Connected");
                
                // Start Listen Loop
                _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
            }
            catch (Exception ex)
            {
                OnStatusChanged?.Invoke($"Error: {ex.Message}");
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            var buffer = new byte[8192];

            while (!token.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                try
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        var payload = JsonConvert.DeserializeObject<TickerPayload>(json);

                        if (payload?.Data != null)
                        {
                            OnTickReceived?.Invoke(payload.Data);
                        }
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        OnStatusChanged?.Invoke("Disconnected");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Ticker Error: {ex.Message}");
                    break;
                }
            }
        }

        public async Task DisconnectAsync()
        {
            _cts?.Cancel();
            if (_ws != null)
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "User Stop", CancellationToken.None);
                _ws.Dispose();
            }
            OnStatusChanged?.Invoke("Stopped");
        }
    }
}
