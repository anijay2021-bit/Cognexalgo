using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cognexalgo.Core.Models;
using Newtonsoft.Json;

namespace Cognexalgo.Core.Services
{
    /// <summary>
    /// Angel One SmartStream (Binary WebSocket) Implementation.
    /// Provides "Massive Coverage" and low-latency ticks.
    /// </summary>
    public class SmartStreamService : IDisposable
    {
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private readonly string _wsUrl = "wss://smartapis.angelone.in/smartstream";
        
        private string _jwtToken;
        private string _feedToken;
        private string _apiKey;
        private string _clientCode;

        public event Action<TickerData> OnTickReceived;
        public event Action<string> OnStatusChanged;

        private readonly ConcurrentDictionary<string, double> _lastLtps = new();
        private List<string> _subscribedTokens = new();

        public bool IsConnected => _ws != null && _ws.State == WebSocketState.Open;

        public SmartStreamService()
        {
        }

        public void SetCredentials(string jwtToken, string feedToken, string apiKey, string clientCode)
        {
            _jwtToken = jwtToken;
            _feedToken = feedToken;
            _apiKey = apiKey;
            _clientCode = clientCode;
        }

        public async Task ConnectAsync()
        {
            if (IsConnected) return;

            _cts = new CancellationTokenSource();
            _ws = new ClientWebSocket();
            
            // Add Custom Headers required by Angel One
            _ws.Options.SetRequestHeader("Authorization", _jwtToken);
            _ws.Options.SetRequestHeader("x-api-key", _apiKey);
            _ws.Options.SetRequestHeader("x-feed-token", _feedToken);
            _ws.Options.SetRequestHeader("x-client-code", _clientCode);

            try
            {
                OnStatusChanged?.Invoke("Connecting to SmartStream...");
                await _ws.ConnectAsync(new Uri(_wsUrl), _cts.Token);
                OnStatusChanged?.Invoke("Connected to SmartStream");
                
                _ = ReceiveLoopAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                OnStatusChanged?.Invoke($"SmartStream Error: {ex.Message}");
            }
        }

        public async Task SubscribeAsync(List<string> tokens, string exchange = "NSE")
        {
            if (!IsConnected) return;

            // Angel One SmartStream Subscription JSON
            // Note: 'params' is a C# reserved keyword, so we use a Dictionary
            int exchangeType = exchange == "NSE" ? 1 : 2;
            var tokenListItems = tokens.Select(t => new Dictionary<string, object>
            {
                { "exchangeType", exchangeType },
                { "tokens", new[] { t } }
            }).ToList();

            var request = new Dictionary<string, object>
            {
                { "correlationID", Guid.NewGuid().ToString() },
                { "action", 1 },
                { "params", new Dictionary<string, object>
                    {
                        { "mode", 1 },
                        { "tokenList", tokenListItems }
                    }
                }
            };

            var json = JsonConvert.SerializeObject(request);
            var buffer = Encoding.UTF8.GetBytes(json);
            
            await _ws.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, _cts.Token);
            
            lock (_subscribedTokens)
            {
                _subscribedTokens.AddRange(tokens);
                _subscribedTokens = _subscribedTokens.Distinct().ToList();
            }
            
            OnStatusChanged?.Invoke($"Subscribed to {tokens.Count} tokens");
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            var buffer = new byte[4096];

            while (!token.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                try
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        ParseBinaryPacket(buffer, result.Count);
                    }
                }
                catch (Exception ex)
                {
                    OnStatusChanged?.Invoke($"Stream Receive Error: {ex.Message}");
                    break;
                }
            }
        }

        private void ParseBinaryPacket(byte[] data, int length)
        {
            // Simplified Angel One SmartStream LTP Parser
            // Packet structure (simplified for LTP mode):
            // Byte 0: Subscription Mode
            // Byte 1-25: Token
            // Byte 43-46: LTP (4 bytes int, divide by 100)
            
            try 
            {
                if (length < 43) return;

                // Extract Token (first 25 bytes, null terminated or padded)
                string token = Encoding.ASCII.GetString(data, 1, 25).TrimEnd('\0', ' ');
                
                // Extract LTP (at offset 43, 4 bytes)
                int ltpInt = BitConverter.ToInt32(data, 43);
                double ltp = ltpInt / 100.0;

                if (ltp > 0)
                {
                    _lastLtps[token] = ltp;
                    
                    // Construct TickerData for the app
                    // Mapping tokens to Index fields
                    var tickerData = new TickerData();
                    
                    if (token == "99926000") tickerData.Nifty = new InstrumentInfo { Ltp = ltp };
                    else if (token == "99926009") tickerData.BankNifty = new InstrumentInfo { Ltp = ltp };
                    else if (token == "99926037") tickerData.FinNifty = new InstrumentInfo { Ltp = ltp };
                    else if (token == "99926030") tickerData.MidcpNifty = new InstrumentInfo { Ltp = ltp };
                    else if (token == "99919017") tickerData.Sensex = new InstrumentInfo { Ltp = ltp };
                    
                    if (tickerData.Nifty != null || tickerData.BankNifty != null || tickerData.FinNifty != null || tickerData.MidcpNifty != null || tickerData.Sensex != null)
                    {
                        OnTickReceived?.Invoke(tickerData);
                    }
                }
            }
            catch { /* Ignore malformed packets */ }
        }

        public double GetLastLtp(string token)
        {
            return _lastLtps.TryGetValue(token, out var ltp) ? ltp : 0;
        }

        public async Task DisconnectAsync()
        {
            _cts?.Cancel();
            if (_ws != null)
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                _ws.Dispose();
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _ws?.Dispose();
        }
    }
}
