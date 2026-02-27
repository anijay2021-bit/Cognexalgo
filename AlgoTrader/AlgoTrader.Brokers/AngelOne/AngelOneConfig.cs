namespace AlgoTrader.Brokers.AngelOne;

/// <summary>Angel One SmartAPI endpoint configuration.</summary>
public record AngelOneConfig
{
    public string BaseUrl { get; init; } = "https://apiconnect.angelbroking.com";
    public string LoginEndpoint { get; init; } = "/rest/auth/angelbroking/user/v1/loginByPassword";
    public string RefreshTokenEndpoint { get; init; } = "/rest/auth/angelbroking/jwt/v1/generateTokens";
    public string ProfileEndpoint { get; init; } = "/rest/secure/angelbroking/user/v1/getProfile";
    public string FundsEndpoint { get; init; } = "/rest/secure/angelbroking/user/v1/getRMS";
    public string OrderEndpoint { get; init; } = "/rest/secure/angelbroking/order/v1/placeOrder";
    public string ModifyOrderEndpoint { get; init; } = "/rest/secure/angelbroking/order/v1/modifyOrder";
    public string CancelOrderEndpoint { get; init; } = "/rest/secure/angelbroking/order/v1/cancelOrder";
    public string OrderBookEndpoint { get; init; } = "/rest/secure/angelbroking/order/v1/getOrderBook";
    public string TradeBookEndpoint { get; init; } = "/rest/secure/angelbroking/order/v1/getTradeBook";
    public string PositionEndpoint { get; init; } = "/rest/secure/angelbroking/order/v1/getPosition";
    public string HistoricalEndpoint { get; init; } = "/rest/secure/angelbroking/historical/v1/getCandleData";
    public string LTPEndpoint { get; init; } = "/rest/secure/angelbroking/market/v1/quote";
}

/// <summary>Well-known instrument tokens for Angel One indices.</summary>
public static class AngelOneTokens
{
    public const string NIFTY50 = "26000";
    public const string BANKNIFTY = "26009";
    public const string FINNIFTY = "26037";
    public const string MIDCPNIFTY = "26074";
    public const string SENSEX = "1";
    public const string BANKEX = "12";
}
