using System.Net;
using RestSharp;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using AlgoTrader.Core.Models;

namespace AlgoTrader.Brokers.AngelOne;

/// <summary>Low-level REST client for Angel One SmartAPI calls.</summary>
public class AngelOneRestClient
{
    private readonly AngelOneConfig _config;
    private readonly RestClient _client;
    private readonly ILogger _logger;
    private readonly string _apiKey;

    private static string? _cachedLocalIp;
    private static string? _cachedMacAddress;

    public AngelOneRestClient(AngelOneConfig config, string apiKey, ILogger logger)
    {
        _config = config;
        _apiKey = apiKey;
        _logger = logger;
        _client = new RestClient(new RestClientOptions(config.BaseUrl) { 
            Proxy = null,
            UserAgent = "AlgoTraderPro/1.0",
            Timeout = TimeSpan.FromSeconds(15)
        });
    }

    /// <summary>Add Angel One required auth headers.</summary>
    private void AddAuthHeaders(RestRequest request, string jwtToken)
    {
        if (request == null) return;

        request.AddHeader("Content-Type", "application/json");
        request.AddHeader("Accept", "application/json");
        request.AddHeader("X-User-Type", "USER");
        request.AddHeader("X-SourceID", "WEB");
        request.AddHeader("X-ClientLocalIP", "127.0.0.1");
        request.AddHeader("X-ClientPublicIP", "127.0.0.1");
        request.AddHeader("X-MACAddress", "MAC_ADDRESS");
        
        if (!string.IsNullOrEmpty(_apiKey))
            request.AddHeader("X-PrivateKey", _apiKey);

        if (!string.IsNullOrEmpty(jwtToken))
            request.AddHeader("Authorization", $"Bearer {jwtToken}");
    }

    public async Task<T?> PostAsync<T>(string endpoint, object body, string token = "")
    {
        var request = new RestRequest(endpoint, Method.Post);
        AddAuthHeaders(request, token);
        
        string json = JsonConvert.SerializeObject(body);
        request.AddStringBody(json, DataFormat.Json);

        _logger.LogDebug("Angel POST {Endpoint}", endpoint);
        var response = await _client.ExecuteAsync(request);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new AngelOneApiException(401, "TOKEN_EXPIRED", "JWT token expired");

        if (!response.IsSuccessful)
        {
            _logger.LogError("Angel POST failed: {StatusCode} {Content}", response.StatusCode, response.Content);
            throw new AngelOneApiException((int)response.StatusCode, "HTTP_ERROR", response.Content ?? "Unknown error");
        }

        return JsonConvert.DeserializeObject<T>(response.Content ?? "{}");
    }

    public async Task<T?> GetAsync<T>(string endpoint, string token)
    {
        var request = new RestRequest(endpoint, Method.Get);
        AddAuthHeaders(request, token);

        _logger.LogDebug("Angel GET {Endpoint}", endpoint);
        var response = await _client.ExecuteAsync(request);

        if (!response.IsSuccessful)
            throw new AngelOneApiException((int)response.StatusCode, "HTTP_ERROR", response.Content ?? "Unknown error");

        return JsonConvert.DeserializeObject<T>(response.Content ?? "{}");
    }

    // ── Portfolio endpoints ──────────────────────────────────────────────────

    /// <summary>Fetch demat holdings from Angel One REST API.</summary>
    public async Task<List<HoldingRecord>> GetHoldingsAsync(string token)
    {
        // Angel One holdings endpoint returns {"status":true,"data":[...]}
        var wrapper = await GetAsync<AngelApiResponse<List<AngelHoldingDto>>>(
            "/rest/secure/angelbroking/portfolio/v1/getHolding", token);

        return wrapper?.Data?.Select(h => new HoldingRecord
        {
            TradingSymbol = h.TradingSymbol,
            ISIN          = h.Isin,
            Exchange      = h.Exchange,
            Qty           = h.Quantity,
            AuthorisedQty = h.AuthorisedQuantity,
            AvgPrice      = h.AveragePrice,
            LTP           = h.Ltp,
            PnL           = h.ProfitAndLoss,
            PnLPercent    = h.Pnlpercentage
        }).ToList() ?? new List<HoldingRecord>();
    }

    /// <summary>Fetch intraday trade book (filled orders only) from Angel One REST API.</summary>
    public async Task<List<TradeRecord>> GetTradeBookAsync(string token)
    {
        var wrapper = await GetAsync<AngelApiResponse<List<AngelTradeDto>>>(
            "/rest/secure/angelbroking/order/v1/getTradeBook", token);

        return wrapper?.Data?.Select(t => new TradeRecord
        {
            OrderId         = t.OrderId,
            TradeId         = t.TradeId,
            TradingSymbol   = t.TradingSymbol,
            Exchange        = t.Exchange,
            TransactionType = t.TransactionType,
            Qty             = t.Quantity,
            FillPrice       = t.TradePrice,
            FillTime        = t.FillTime,
            ProductType     = t.ProductType
        }).ToList() ?? new List<TradeRecord>();
    }

    private static string GetLocalIP()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return ip.ToString();
            }
        }
        catch { }
        return "127.0.0.1";
    }

    private static string GetMacAddress()
    {
        try
        {
            return System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                .Select(n => n.GetPhysicalAddress().ToString())
                .FirstOrDefault() ?? "00:00:00:00:00:00";
        }
        catch { return "00:00:00:00:00:00"; }
    }
}

/// <summary>Angel One specific API exception.</summary>
public class AngelOneApiException : Exception
{
    public int StatusCode { get; }
    public string ErrorCode { get; }

    public AngelOneApiException(int statusCode, string errorCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }
}
