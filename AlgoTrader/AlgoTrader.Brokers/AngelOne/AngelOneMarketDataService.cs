using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using AlgoTrader.Core.Enums;
using AlgoTrader.Core.Interfaces;
using AlgoTrader.Core.Models;
using System.Net.WebSockets;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using Websocket.Client;

namespace AlgoTrader.Brokers.AngelOne;

/// <summary>WebSocket config for Angel One SmartStream.</summary>
public record AngelOneWebSocketConfig
{
    public string FeedUrl { get; init; } = "wss://smartapisocket.angelone.in/smart-stream";
    public int HeartbeatIntervalSeconds { get; init; } = 10;  // Angel One server drops conn after ~20s of no ping
    public int ReconnectDelaySeconds { get; init; } = 5;
}

/// <summary>Angel One live market data via WebSocket implementing IMarketDataService.</summary>
public class AngelOneMarketDataService : IMarketDataService, IDisposable
{
    private readonly ILogger<AngelOneMarketDataService> _logger;
    private readonly AngelOneWebSocketConfig _wsConfig;
    private WebsocketClient? _wsClient;
    private readonly Subject<Tick> _tickSubject = new();
    private readonly Dictionary<string, (Exchange Exchange, SubscriptionMode Mode)> _activeSubscriptions = new();
    private PeriodicTimer? _heartbeatTimer;
    private CancellationTokenSource? _heartbeatCts;
    private AccountCredential? _credential;
    private IDisposable? _messageSubscription;

    // Always-on LTP subscriptions: keeps the WS session alive on the server side
    // and feeds the header bar index prices. Never removed by UnsubscribeAsync.
    private static readonly List<(Exchange Exchange, string Token)> _keepAliveTokens = new()
    {
        (Exchange.NSE, "26000"),   // NIFTY 50
        (Exchange.NSE, "26009"),   // BANKNIFTY
        (Exchange.NSE, "26037"),   // FINNIFTY
    };

    public IObservable<Tick> TickStream => _tickSubject.AsObservable();
    public bool IsConnected => _wsClient?.IsRunning ?? false;

    public AngelOneMarketDataService(ILogger<AngelOneMarketDataService> logger)
    {
        _logger = logger;
        _wsConfig = new AngelOneWebSocketConfig();
    }

    public async Task ConnectAsync(AccountCredential credential)
    {
        _credential = credential;
        var url = new Uri(_wsConfig.FeedUrl);

        var clientFactory = new Func<ClientWebSocket>(() =>
        {
            var client = new ClientWebSocket();
            client.Options.SetRequestHeader("Authorization", credential.JWTToken);
            client.Options.SetRequestHeader("x-api-key", credential.APIKey);
            client.Options.SetRequestHeader("x-client-code", credential.ClientID);
            client.Options.SetRequestHeader("x-feed-token", credential.FeedToken);
            return client;
        });

        _wsClient = new WebsocketClient(url, clientFactory)
        {
            // null = don't reconnect just because no ticks are flowing (e.g. no subscriptions, non-trading hours).
            // Reconnection is handled by ErrorReconnectTimeout on actual errors.
            ReconnectTimeout = null,
            ErrorReconnectTimeout = TimeSpan.FromSeconds(_wsConfig.ReconnectDelaySeconds)
        };

        _wsClient.ReconnectionHappened.Subscribe(info =>
        {
            _logger.LogInformation("Angel WS reconnected: {Type}", info.Type);
            // Re-auth + re-subscribe after reconnect.
            // Small delay lets the server finish the WS upgrade before it accepts messages.
            Task.Delay(300).ContinueWith(_ =>
            {
                SendAuthHandshake(_credential!);
                SubscribeKeepAlive();
                ResubscribeAll();
            });
        });

        _wsClient.DisconnectionHappened.Subscribe(info =>
        {
            _logger.LogWarning("Angel WS disconnected: {Type} | Status={Status} | Desc={Desc} | Ex={Ex}",
                info.Type, info.CloseStatus, info.CloseStatusDescription, info.Exception?.Message);
        });

        _messageSubscription = _wsClient.MessageReceived.Subscribe(msg =>
        {
            if (msg.MessageType == System.Net.WebSockets.WebSocketMessageType.Binary && msg.Binary != null)
            {
                var ticks = AngelOneFeedParser.ParseBinaryFrame(msg.Binary);
                foreach (var tick in ticks)
                    _tickSubject.OnNext(tick);
            }
            else if (msg.MessageType == System.Net.WebSockets.WebSocketMessageType.Text)
            {
                _logger.LogDebug("Angel WS text: {Text}", msg.Text);
            }
        });

        await _wsClient.Start();

        // Auth message required by Angel One SmartStream in addition to HTTP headers.
        SendAuthHandshake(credential);

        // Allow ~500ms for the server to process auth before sending subscriptions.
        await Task.Delay(500);

        // Subscribe index tokens immediately — prevents the server's ~20s idle-session
        // timeout and also feeds the header bar with live NIFTY/BN/FIN prices.
        SubscribeKeepAlive();

        StartHeartbeat();
        _logger.LogInformation("Angel One WebSocket connected for {ClientID}", credential.ClientID);
    }


    private void SendAuthHandshake(AccountCredential credential)
    {
        var authMsg = JsonConvert.SerializeObject(new
        {
            task    = "cn",
            channel = "",
            token   = credential.FeedToken,
            user    = credential.ClientID,
            acctid  = credential.ClientID
        });
        _wsClient?.Send(authMsg);
        _logger.LogDebug("Angel WS auth handshake sent");
    }

    public Task SubscribeAsync(List<(Exchange exchange, string token)> symbols, SubscriptionMode mode)
    {
        if (_wsClient == null || !_wsClient.IsRunning) return Task.CompletedTask;

        // Group tokens by exchange type
        var tokensByExchange = symbols.GroupBy(s => MapExchangeToInt(s.exchange));

        var tokenList = tokensByExchange.Select(g => new
        {
            exchangeType = g.Key,
            tokens = g.Select(s => s.token).ToList()
        }).ToList();

        var modeStr = mode switch
        {
            SubscriptionMode.LTP => 1,
            SubscriptionMode.QUOTE => 2,
            SubscriptionMode.FULL => 3,
            _ => 1
        };

        var subscribeMsg = JsonConvert.SerializeObject(new
        {
            action = 1,  // subscribe
            @params = new
            {
                mode = modeStr,
                tokenList = tokenList
            }
        });

        _wsClient.Send(subscribeMsg);

        // Track active subscriptions
        foreach (var (exchange, token) in symbols)
        {
            _activeSubscriptions[token] = (exchange, mode);
        }

        _logger.LogInformation("Angel WS subscribed to {Count} symbols in {Mode} mode", symbols.Count, mode);
        return Task.CompletedTask;
    }

    public Task<Dictionary<string, decimal>> GetBatchLTPAsync(List<(Exchange exchange, string token)> tokens, string authToken)
    {
        // For accurate batch LTP, usually a REST API is called.
        // Returning empty dictionary as dummy implementation for now.
        return Task.FromResult(new Dictionary<string, decimal>());
    }

    public Task UnsubscribeAsync(List<(Exchange exchange, string token)> symbols)
    {
        if (_wsClient == null || !_wsClient.IsRunning) return Task.CompletedTask;

        // Never unsubscribe keep-alive tokens — they are managed internally.
        var keepAliveSet = new HashSet<string>(_keepAliveTokens.Select(k => k.Token));
        symbols = symbols.Where(s => !keepAliveSet.Contains(s.token)).ToList();
        if (symbols.Count == 0) return Task.CompletedTask;

        var tokensByExchange = symbols.GroupBy(s => MapExchangeToInt(s.exchange));

        var tokenList = tokensByExchange.Select(g => new
        {
            exchangeType = g.Key,
            tokens = g.Select(s => s.token).ToList()
        }).ToList();

        var unsubMsg = JsonConvert.SerializeObject(new
        {
            action = 0,  // unsubscribe
            @params = new
            {
                mode = 1,
                tokenList = tokenList
            }
        });

        _wsClient.Send(unsubMsg);

        foreach (var (_, token) in symbols)
            _activeSubscriptions.Remove(token);

        _logger.LogInformation("Angel WS unsubscribed from {Count} symbols", symbols.Count);
        return Task.CompletedTask;
    }

    private void SubscribeKeepAlive()
    {
        _ = SubscribeAsync(_keepAliveTokens, SubscriptionMode.LTP);
    }

    private void ResubscribeAll()
    {
        if (_activeSubscriptions.Count == 0) return;

        var grouped = _activeSubscriptions
            .GroupBy(kvp => kvp.Value.Mode)
            .ToList();

        foreach (var group in grouped)
        {
            var symbols = group.Select(kvp => (kvp.Value.Exchange, kvp.Key)).ToList();
            _ = SubscribeAsync(symbols, group.Key);
        }
    }

    private void StartHeartbeat()
    {
        _heartbeatCts = new CancellationTokenSource();
        _heartbeatTimer = new PeriodicTimer(TimeSpan.FromSeconds(_wsConfig.HeartbeatIntervalSeconds));

        _ = Task.Run(async () =>
        {
            while (await _heartbeatTimer.WaitForNextTickAsync(_heartbeatCts.Token))
            {
                try
                {
                    if (_wsClient?.IsRunning == true)
                        _wsClient.Send("ping");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Angel WS heartbeat failed");
                }
            }
        });
    }

    public async Task DisconnectAsync()
    {
        _heartbeatCts?.Cancel();
        _heartbeatTimer?.Dispose();
        _messageSubscription?.Dispose();

        if (_wsClient != null)
        {
            await _wsClient.Stop(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "Closing");
            _wsClient.Dispose();
        }

        _activeSubscriptions.Clear();
        _logger.LogInformation("Angel One WebSocket disconnected");
    }

    private static int MapExchangeToInt(Exchange exchange) => exchange switch
    {
        Exchange.NSE => 1,
        Exchange.NFO => 2,
        Exchange.BSE => 3,
        Exchange.MCX => 5,
        Exchange.CDS => 13,
        _ => 1
    };

    public void Dispose()
    {
        _heartbeatCts?.Cancel();
        _heartbeatTimer?.Dispose();
        _messageSubscription?.Dispose();
        _wsClient?.Dispose();
        _tickSubject.Dispose();
    }
}

/// <summary>Parses Angel One binary WebSocket frames into Tick objects.</summary>
public static class AngelOneFeedParser
{
    public static List<Tick> ParseBinaryFrame(byte[] data)
    {
        var ticks = new List<Tick>();

        try
        {
            if (data.Length < 4) return ticks;

            // Angel One binary frame: subscription mode (1), exchange type (1),
            // token (25 bytes), sequence (8), exchange timestamp (8), LTP (4)
            // For QUOTE & FULL modes: additional OHLC, volume, etc.

            int offset = 0;
            while (offset + 30 <= data.Length) // minimum frame size
            {
                var tick = new Tick();

                byte mode = data[offset]; // 1=LTP, 2=QUOTE, 3=FULL
                byte exchangeType = data[offset + 1];
                tick.Exchange = MapExchange(exchangeType);

                // Token: bytes 2-26 (25 bytes, string, null-terminated)
                tick.Token = Encoding.ASCII.GetString(data, offset + 2, 25).TrimEnd('\0').Trim();

                // Map index tokens to symbols for UI filtering in MainForm
                if (tick.Token == "26000") tick.Symbol = "NIFTY";
                else if (tick.Token == "26009") tick.Symbol = "BANKNIFTY";
                else if (tick.Token == "26037") tick.Symbol = "FINNIFTY";

                // Sequence number: bytes 27-34 (8 bytes, little-endian long)
                // Exchange timestamp: bytes 35-42
                // For simplicity, parse LTP at minimum

                if (offset + 42 <= data.Length)
                {
                    long seqNo = BitConverter.ToInt64(data, offset + 27);
                    long exchTimestamp = BitConverter.ToInt64(data, offset + 35);
                    tick.Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(exchTimestamp).DateTime;
                }

                // LTP: bytes 43-46 (4 bytes int, divide by 100)
                if (offset + 47 <= data.Length)
                {
                    int ltpRaw = BitConverter.ToInt32(data, offset + 43);
                    tick.LTP = ltpRaw / 100m;
                }

                // Additional fields for QUOTE/FULL mode
                int frameSize = mode switch
                {
                    1 => 47,      // LTP mode
                    2 => 83,      // QUOTE mode (adds OHLC, volume)
                    3 => 123,     // FULL mode (adds bid/ask depth)
                    _ => 47
                };

                if (mode >= 2 && offset + 83 <= data.Length)
                {
                    tick.Volume = BitConverter.ToInt64(data, offset + 67);
                    tick.OI = BitConverter.ToInt64(data, offset + 75);
                }

                if (mode >= 3 && offset + 99 <= data.Length)
                {
                    tick.BidPrice = BitConverter.ToInt32(data, offset + 83) / 100m;
                    tick.BidQty = BitConverter.ToInt32(data, offset + 87);
                    tick.AskPrice = BitConverter.ToInt32(data, offset + 91) / 100m;
                    tick.AskQty = BitConverter.ToInt32(data, offset + 95);
                }

                ticks.Add(tick);
                offset += frameSize;
            }
        }
        catch (Exception)
        {
            // Malformed frame — skip
        }

        return ticks;
    }

    private static Exchange MapExchange(byte exchangeType) => exchangeType switch
    {
        1 => Exchange.NSE,
        2 => Exchange.NFO,
        3 => Exchange.BSE,
        5 => Exchange.MCX,
        13 => Exchange.CDS,
        _ => Exchange.NSE
    };
}
