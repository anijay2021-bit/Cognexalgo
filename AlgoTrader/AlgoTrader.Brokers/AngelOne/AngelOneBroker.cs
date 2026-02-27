using AlgoTrader.Core.Enums;
using AlgoTrader.Core.Interfaces;
using AlgoTrader.Core.Models;
using Microsoft.Extensions.Logging;

namespace AlgoTrader.Brokers.AngelOne;

/// <summary>Full Angel One IBroker implementation — composes auth, order, and historical services.</summary>
public class AngelOneBroker : IBroker
{
    private readonly AngelOneConfig _config;
    private readonly ILogger<AngelOneBroker> _logger;
    private AngelOneRestClient? _restClient;
    private Timer? _refreshTimer;

    public BrokerType BrokerType => BrokerType.AngelOne;

    public AngelOneBroker(ILogger<AngelOneBroker> logger)
    {
        _config = new AngelOneConfig();
        _logger = logger;
    }

    public async Task<bool> LoginAsync(AccountCredential credential)
    {
        if (string.IsNullOrEmpty(credential.ClientID) || string.IsNullOrEmpty(credential.APIKey))
        {
            _logger.LogError("Login failed: ClientID or APIKey is missing.");
            return false;
        }

        try
        {
            _restClient = new AngelOneRestClient(_config, credential.APIKey, _logger);

            // Generate TOTP
            string totp = string.Empty;
            if (!string.IsNullOrEmpty(credential.TOTPSecret))
            {
                var key = OtpNet.Base32Encoding.ToBytes(credential.TOTPSecret.Trim().Replace(" ", ""));
                var totpObj = new OtpNet.Totp(key);
                totp = totpObj.ComputeTotp();
            }

            var loginReq = new AngelLoginRequest
            {
                ClientCode = credential.ClientID,
                Password = credential.Password,
                Totp = totp
            };

            var result = await _restClient.PostAsync<AngelApiResponse<AngelLoginData>>(_config.LoginEndpoint, loginReq);
            if (result?.Status != true || result.Data == null)
            {
                _logger.LogError("Angel One login failed for {ClientID}: {Message} (Error: {Code})", 
                    credential.ClientID, result?.Message, result?.ErrorCode);
                return false;
            }

            credential.JWTToken = result.Data.JwtToken;
            credential.RefreshToken = result.Data.RefreshToken;
            credential.FeedToken = result.Data.FeedToken;
            credential.IsLoggedIn = true;
            credential.TokenExpiry = DateTime.UtcNow.AddHours(24);

            // Schedule token refresh 1 hour before expiry
            _refreshTimer = new Timer(async _ => await RefreshTokenAsync(credential),
                null, TimeSpan.FromHours(23), TimeSpan.FromHours(23));

            _logger.LogInformation("Angel One login successful for {ClientID} ({Name})", credential.ClientID, result.Data.Name);
            return true;
        }
        catch (AngelOneApiException ex)
        {
            _logger.LogError("Angel One API Error for {ClientID}: {Message} (Status: {Status}, Code: {Code})", 
                credential.ClientID, ex.Message, ex.StatusCode, ex.ErrorCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected Angel One login error for {ClientID}", credential.ClientID);
            return false;
        }
    }

    public async Task<bool> RefreshTokenAsync(AccountCredential credential)
    {
        try
        {
            if (_restClient == null) return false;

            var result = await _restClient.PostAsync<AngelApiResponse<AngelLoginData>>(
                _config.RefreshTokenEndpoint,
                new AngelRefreshRequest { RefreshToken = credential.RefreshToken });

            if (result?.Status == true && result.Data != null)
            {
                credential.JWTToken = result.Data.JwtToken;
                credential.RefreshToken = result.Data.RefreshToken;
                credential.FeedToken = result.Data.FeedToken;
                credential.TokenExpiry = DateTime.UtcNow.AddHours(24);
                _logger.LogInformation("Angel One token refreshed for {ClientID}", credential.ClientID);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Angel One token refresh failed for {ClientID}", credential.ClientID);
            return false;
        }
    }

    public async Task<UserProfile> GetProfileAsync(string token)
    {
        var result = await _restClient!.GetAsync<AngelApiResponse<AngelProfileData>>(_config.ProfileEndpoint, token);
        var d = result?.Data;
        return new UserProfile
        {
            ClientId = d?.ClientCode ?? "",
            Name = d?.Name ?? "",
            Email = d?.Email ?? "",
            Phone = d?.MobileNo ?? "",
            Broker = "AngelOne",
            Exchanges = d?.Exchanges ?? new()
        };
    }

    public async Task<FundsData> GetFundsAsync(string token)
    {
        var result = await _restClient!.GetAsync<AngelApiResponse<AngelRMSData>>(_config.FundsEndpoint, token);
        var d = result?.Data;
        return new FundsData
        {
            AvailableMargin = decimal.TryParse(d?.AvailableCash, out var ac) ? ac : 0,
            UsedMargin = decimal.TryParse(d?.UtilisedDebits, out var ud) ? ud : 0,
            TotalMargin = decimal.TryParse(d?.Net, out var n) ? n : 0,
            BrokerType = BrokerType.AngelOne
        };
    }

    public async Task<OrderResponse> PlaceOrderAsync(OrderRequest request, string token)
    {
        var dto = MapToAngelOrder(request);
        var result = await _restClient!.PostAsync<AngelApiResponse<AngelOrderResponseData>>(_config.OrderEndpoint, dto, token);
        return new OrderResponse
        {
            OrderID = result?.Data?.OrderId ?? "",
            Status = result?.Status == true ? OrderStatus.OPEN : OrderStatus.REJECTED,
            StatusMessage = result?.Message ?? "",
            Timestamp = DateTime.UtcNow
        };
    }

    public async Task<OrderResponse> ModifyOrderAsync(string orderId, OrderRequest request, string token)
    {
        var dto = new AngelModifyOrderDto
        {
            OrderId = orderId,
            Variety = "NORMAL",
            TradingSymbol = request.Symbol,
            SymbolToken = request.Token,
            Exchange = request.Exchange.ToString(),
            OrderType = request.OrderType.ToString(),
            ProductType = request.ProductType.ToString(),
            Duration = request.Validity,
            Price = request.LimitPrice.ToString("F2"),
            Quantity = request.Qty.ToString(),
            TriggerPrice = request.TriggerPrice.ToString("F2")
        };

        var result = await _restClient!.PostAsync<AngelApiResponse<AngelOrderResponseData>>(_config.ModifyOrderEndpoint, dto, token);
        return new OrderResponse
        {
            OrderID = result?.Data?.OrderId ?? orderId,
            Status = result?.Status == true ? OrderStatus.MODIFIED : OrderStatus.REJECTED,
            StatusMessage = result?.Message ?? "",
            Timestamp = DateTime.UtcNow
        };
    }

    public async Task<OrderResponse> CancelOrderAsync(string orderId, string token)
    {
        var dto = new AngelCancelOrderDto { Variety = "NORMAL", OrderId = orderId };
        var result = await _restClient!.PostAsync<AngelApiResponse<AngelOrderResponseData>>(_config.CancelOrderEndpoint, dto, token);
        return new OrderResponse
        {
            OrderID = orderId,
            Status = result?.Status == true ? OrderStatus.CANCELLED : OrderStatus.REJECTED,
            StatusMessage = result?.Message ?? "",
            Timestamp = DateTime.UtcNow
        };
    }

    public async Task<List<OrderBook>> GetOrderBookAsync(string token)
    {
        var result = await _restClient!.GetAsync<AngelApiResponse<List<AngelOrderBookItem>>>(_config.OrderBookEndpoint, token);
        if (result?.Data == null) return new List<OrderBook>();

        return result.Data.Select(o => new OrderBook
        {
            OrderID = o.OrderId,
            Symbol = o.TradingSymbol,
            Token = o.SymbolToken,
            Exchange = Enum.TryParse<Exchange>(o.Exchange, out var ex) ? ex : Exchange.NSE,
            BuySell = o.TransactionType == "BUY" ? BuySell.BUY : BuySell.SELL,
            OrderQty = int.TryParse(o.Quantity, out var q) ? q : 0,
            FilledQty = int.TryParse(o.FilledShares, out var f) ? f : 0,
            AvgPrice = decimal.TryParse(o.AveragePrice, out var ap) ? ap : 0,
            LimitPrice = decimal.TryParse(o.Price, out var p) ? p : 0,
            TriggerPrice = decimal.TryParse(o.TriggerPrice, out var tp) ? tp : 0,
            OrderType = Enum.TryParse<OrderType>(o.OrderType, out var ot) ? ot : OrderType.MARKET,
            ProductType = Enum.TryParse<ProductType>(o.ProductType, out var pt) ? pt : ProductType.NRML,
            Status = MapOrderStatus(o.Status),
            StatusMessage = o.Text,
            Tag = o.OrderTag,
            Broker = BrokerType.AngelOne,
            Strike = decimal.TryParse(o.StrikePrice, out var sp) ? sp : 0,
            PlaceTime = DateTime.TryParse(o.UpdateTime, out var ut) ? ut : DateTime.UtcNow,
            OrderTime = DateTime.TryParse(o.UpdateTime, out var ot2) ? ot2 : DateTime.UtcNow,
        }).ToList();
    }

    public async Task<List<Position>> GetPositionBookAsync(string token)
    {
        var result = await _restClient!.GetAsync<AngelApiResponse<List<AngelPositionItem>>>(_config.PositionEndpoint, token);
        if (result?.Data == null) return new List<Position>();

        return result.Data.Select(p => new Position
        {
            Symbol = p.TradingSymbol,
            Token = p.SymbolToken,
            Exchange = Enum.TryParse<Exchange>(p.Exchange, out var ex) ? ex : Exchange.NSE,
            ProductType = Enum.TryParse<ProductType>(p.ProductType, out var pt) ? pt : ProductType.NRML,
            NetQty = int.TryParse(p.NetQty, out var nq) ? nq : 0,
            BuyQty = int.TryParse(p.BuyQty, out var bq) ? bq : 0,
            SellQty = int.TryParse(p.SellQty, out var sq) ? sq : 0,
            BuyAvg = decimal.TryParse(p.BuyAvgPrice, out var ba) ? ba : 0,
            SellAvg = decimal.TryParse(p.SellAvgPrice, out var sa) ? sa : 0,
            LTP = decimal.TryParse(p.Ltp, out var ltp) ? ltp : 0,
            MTM = decimal.TryParse(p.Pnl, out var pnl) ? pnl : 0,
            RealizedPnL = decimal.TryParse(p.Realised, out var r) ? r : 0,
            BrokerType = BrokerType.AngelOne
        }).ToList();
    }

    public async Task<List<Candle>> GetHistoricalDataAsync(string token, string symbolToken, Exchange exchange, TimeFrame interval, DateTime from, DateTime to)
    {
        var req = new AngelHistoricalRequest
        {
            Exchange = exchange.ToString(),
            SymbolToken = symbolToken,
            Interval = interval.ToString(),
            FromDate = from.ToString("yyyy-MM-dd HH:mm"),
            ToDate = to.ToString("yyyy-MM-dd HH:mm")
        };

        var result = await _restClient!.PostAsync<AngelApiResponse<List<List<string>>>>(_config.HistoricalEndpoint, req, token);
        if (result?.Data == null) return new List<Candle>();

        return result.Data.Select(row => new Candle
        {
            Token = symbolToken,
            Interval = interval,
            Timestamp = DateTime.TryParse(row.ElementAtOrDefault(0), out var ts) ? ts : DateTime.MinValue,
            Open = decimal.TryParse(row.ElementAtOrDefault(1), out var o) ? o : 0,
            High = decimal.TryParse(row.ElementAtOrDefault(2), out var h) ? h : 0,
            Low = decimal.TryParse(row.ElementAtOrDefault(3), out var l) ? l : 0,
            Close = decimal.TryParse(row.ElementAtOrDefault(4), out var c) ? c : 0,
            Volume = long.TryParse(row.ElementAtOrDefault(5), out var v) ? v : 0,
        }).ToList();
    }

    public async Task<decimal> GetLTPAsync(string symbolToken, Exchange exchange, string authToken)
    {
        var req = new AngelLTPRequest
        {
            Mode = "LTP",
            ExchangeTokens = new Dictionary<string, List<string>>
            {
                { exchange.ToString(), new List<string> { symbolToken } }
            }
        };

        var result = await _restClient!.PostAsync<AngelApiResponse<Dictionary<string, List<Dictionary<string, object>>>>>(_config.LTPEndpoint, req, authToken);
        if (result?.Data == null) return 0;

        // Extract LTP from response
        foreach (var kvp in result.Data)
        {
            foreach (var item in kvp.Value)
            {
                if (item.TryGetValue("ltp", out var ltp) && decimal.TryParse(ltp?.ToString(), out var val))
                    return val;
            }
        }
        return 0;
    }

    // ─── Private helpers ───

    private AngelOrderRequestDto MapToAngelOrder(OrderRequest request)
    {
        string variety = request.OrderType switch
        {
            OrderType.SL or OrderType.SL_M => "STOPLOSS",
            _ => "NORMAL"
        };

        return new AngelOrderRequestDto
        {
            Variety = variety,
            TradingSymbol = request.Symbol,
            SymbolToken = request.Token,
            TransactionType = request.BuySell.ToString(),
            Exchange = request.Exchange.ToString(),
            OrderType = request.OrderType.ToString(),
            ProductType = request.ProductType.ToString(),
            Duration = request.Validity,
            Price = request.LimitPrice.ToString("F2"),
            Quantity = request.Qty.ToString(),
            TriggerPrice = request.TriggerPrice.ToString("F2"),
            OrderTag = request.Tag
        };
    }

    private static OrderStatus MapOrderStatus(string status)
    {
        return status?.ToUpper() switch
        {
            "OPEN" or "PENDING" => OrderStatus.OPEN,
            "COMPLETE" or "TRADED" => OrderStatus.COMPLETE,
            "CANCELLED" => OrderStatus.CANCELLED,
            "REJECTED" => OrderStatus.REJECTED,
            "TRIGGER PENDING" or "TRIGGER_PENDING" => OrderStatus.TRIGGER_PENDING,
            _ => OrderStatus.OPEN
        };
    }

    // ── Portfolio queries ────────────────────────────────────────────────────

    public async Task<List<HoldingRecord>> GetHoldingsAsync(string token)
    {
        if (_restClient == null) return new List<HoldingRecord>();
        try
        {
            return await _restClient.GetHoldingsAsync(token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetHoldings failed");
            return new List<HoldingRecord>();
        }
    }

    public async Task<List<TradeRecord>> GetTradeBookAsync(string token)
    {
        if (_restClient == null) return new List<TradeRecord>();
        try
        {
            return await _restClient.GetTradeBookAsync(token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetTradeBook failed");
            return new List<TradeRecord>();
        }
    }
}
