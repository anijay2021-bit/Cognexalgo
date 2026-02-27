using Newtonsoft.Json;

namespace AlgoTrader.Brokers.AngelOne;

// ─── Request DTOs ───

public class AngelLoginRequest
{
    [JsonProperty("clientcode")] public string ClientCode { get; set; } = string.Empty;
    [JsonProperty("password")] public string Password { get; set; } = string.Empty;
    [JsonProperty("totp")] public string Totp { get; set; } = string.Empty;
}

public class AngelRefreshRequest
{
    [JsonProperty("refreshToken")] public string RefreshToken { get; set; } = string.Empty;
}

public class AngelOrderRequestDto
{
    [JsonProperty("variety")] public string Variety { get; set; } = "NORMAL";
    [JsonProperty("tradingsymbol")] public string TradingSymbol { get; set; } = string.Empty;
    [JsonProperty("symboltoken")] public string SymbolToken { get; set; } = string.Empty;
    [JsonProperty("transactiontype")] public string TransactionType { get; set; } = string.Empty;
    [JsonProperty("exchange")] public string Exchange { get; set; } = string.Empty;
    [JsonProperty("ordertype")] public string OrderType { get; set; } = string.Empty;
    [JsonProperty("producttype")] public string ProductType { get; set; } = string.Empty;
    [JsonProperty("duration")] public string Duration { get; set; } = "DAY";
    [JsonProperty("price")] public string Price { get; set; } = "0";
    [JsonProperty("squareoff")] public string Squareoff { get; set; } = "0";
    [JsonProperty("stoploss")] public string Stoploss { get; set; } = "0";
    [JsonProperty("quantity")] public string Quantity { get; set; } = "0";
    [JsonProperty("triggerprice")] public string TriggerPrice { get; set; } = "0";
    [JsonProperty("ordertag")] public string OrderTag { get; set; } = string.Empty;
}

public class AngelModifyOrderDto
{
    [JsonProperty("variety")] public string Variety { get; set; } = "NORMAL";
    [JsonProperty("orderid")] public string OrderId { get; set; } = string.Empty;
    [JsonProperty("ordertype")] public string OrderType { get; set; } = string.Empty;
    [JsonProperty("producttype")] public string ProductType { get; set; } = string.Empty;
    [JsonProperty("duration")] public string Duration { get; set; } = "DAY";
    [JsonProperty("price")] public string Price { get; set; } = "0";
    [JsonProperty("quantity")] public string Quantity { get; set; } = "0";
    [JsonProperty("triggerprice")] public string TriggerPrice { get; set; } = "0";
    [JsonProperty("tradingsymbol")] public string TradingSymbol { get; set; } = string.Empty;
    [JsonProperty("symboltoken")] public string SymbolToken { get; set; } = string.Empty;
    [JsonProperty("exchange")] public string Exchange { get; set; } = string.Empty;
}

public class AngelCancelOrderDto
{
    [JsonProperty("variety")] public string Variety { get; set; } = "NORMAL";
    [JsonProperty("orderid")] public string OrderId { get; set; } = string.Empty;
}

public class AngelHistoricalRequest
{
    [JsonProperty("exchange")] public string Exchange { get; set; } = string.Empty;
    [JsonProperty("symboltoken")] public string SymbolToken { get; set; } = string.Empty;
    [JsonProperty("interval")] public string Interval { get; set; } = string.Empty;
    [JsonProperty("fromdate")] public string FromDate { get; set; } = string.Empty;
    [JsonProperty("todate")] public string ToDate { get; set; } = string.Empty;
}

public class AngelLTPRequest
{
    [JsonProperty("mode")] public string Mode { get; set; } = "LTP";
    [JsonProperty("exchangeTokens")] public Dictionary<string, List<string>> ExchangeTokens { get; set; } = new();
}

// ─── Response DTOs ───

public class AngelApiResponse<T>
{
    [JsonProperty("status")] public bool Status { get; set; }
    [JsonProperty("message")] public string Message { get; set; } = string.Empty;
    [JsonProperty("errorcode")] public string ErrorCode { get; set; } = string.Empty;
    [JsonProperty("data")] public T? Data { get; set; }
}

public class AngelLoginData
{
    [JsonProperty("jwtToken")] public string JwtToken { get; set; } = string.Empty;
    [JsonProperty("refreshToken")] public string RefreshToken { get; set; } = string.Empty;
    [JsonProperty("feedToken")] public string FeedToken { get; set; } = string.Empty;
    [JsonProperty("name")] public string Name { get; set; } = string.Empty;
}

public class AngelOrderResponseData
{
    [JsonProperty("orderid")] public string OrderId { get; set; } = string.Empty;
    [JsonProperty("uniqueorderid")] public string UniqueOrderId { get; set; } = string.Empty;
}

public class AngelProfileData
{
    [JsonProperty("clientcode")] public string ClientCode { get; set; } = string.Empty;
    [JsonProperty("name")] public string Name { get; set; } = string.Empty;
    [JsonProperty("email")] public string Email { get; set; } = string.Empty;
    [JsonProperty("mobileno")] public string MobileNo { get; set; } = string.Empty;
    [JsonProperty("exchanges")] public List<string> Exchanges { get; set; } = new();
}

public class AngelRMSData
{
    [JsonProperty("availablecash")] public string AvailableCash { get; set; } = "0";
    [JsonProperty("utiliseddebits")] public string UtilisedDebits { get; set; } = "0";
    [JsonProperty("net")] public string Net { get; set; } = "0";
}

public class AngelOrderBookItem
{
    [JsonProperty("orderid")] public string OrderId { get; set; } = string.Empty;
    [JsonProperty("tradingsymbol")] public string TradingSymbol { get; set; } = string.Empty;
    [JsonProperty("exchange")] public string Exchange { get; set; } = string.Empty;
    [JsonProperty("transactiontype")] public string TransactionType { get; set; } = string.Empty;
    [JsonProperty("quantity")] public string Quantity { get; set; } = "0";
    [JsonProperty("filledshares")] public string FilledShares { get; set; } = "0";
    [JsonProperty("averageprice")] public string AveragePrice { get; set; } = "0";
    [JsonProperty("price")] public string Price { get; set; } = "0";
    [JsonProperty("triggerprice")] public string TriggerPrice { get; set; } = "0";
    [JsonProperty("ordertype")] public string OrderType { get; set; } = string.Empty;
    [JsonProperty("producttype")] public string ProductType { get; set; } = string.Empty;
    [JsonProperty("status")] public string Status { get; set; } = string.Empty;
    [JsonProperty("text")] public string Text { get; set; } = string.Empty;
    [JsonProperty("exchtime")] public string ExchTime { get; set; } = string.Empty;
    [JsonProperty("updatetime")] public string UpdateTime { get; set; } = string.Empty;
    [JsonProperty("symboltoken")] public string SymbolToken { get; set; } = string.Empty;
    [JsonProperty("ordertag")] public string OrderTag { get; set; } = string.Empty;
    [JsonProperty("strikeprice")] public string StrikePrice { get; set; } = "0";
    [JsonProperty("optiontype")] public string OptionType { get; set; } = string.Empty;
}

public class AngelPositionItem
{
    [JsonProperty("tradingsymbol")] public string TradingSymbol { get; set; } = string.Empty;
    [JsonProperty("symboltoken")] public string SymbolToken { get; set; } = string.Empty;
    [JsonProperty("exchange")] public string Exchange { get; set; } = string.Empty;
    [JsonProperty("producttype")] public string ProductType { get; set; } = string.Empty;
    [JsonProperty("netqty")] public string NetQty { get; set; } = "0";
    [JsonProperty("buyqty")] public string BuyQty { get; set; } = "0";
    [JsonProperty("sellqty")] public string SellQty { get; set; } = "0";
    [JsonProperty("buyavgprice")] public string BuyAvgPrice { get; set; } = "0";
    [JsonProperty("sellavgprice")] public string SellAvgPrice { get; set; } = "0";
    [JsonProperty("ltp")] public string Ltp { get; set; } = "0";
    [JsonProperty("pnl")] public string Pnl { get; set; } = "0";
    [JsonProperty("realised")] public string Realised { get; set; } = "0";
}

// ─── Holdings & Trade Book DTOs (added for IBroker.GetHoldingsAsync / GetTradeBookAsync) ───

public class AngelHoldingDto
{
    [JsonProperty("tradingsymbol")]      public string  TradingSymbol      { get; set; } = string.Empty;
    [JsonProperty("isin")]               public string  Isin               { get; set; } = string.Empty;
    [JsonProperty("exchange")]           public string  Exchange           { get; set; } = string.Empty;
    [JsonProperty("quantity")]           public int     Quantity           { get; set; }
    [JsonProperty("authorisedquantity")] public int     AuthorisedQuantity { get; set; }
    [JsonProperty("averageprice")]       public decimal AveragePrice       { get; set; }
    [JsonProperty("ltp")]                public decimal Ltp                { get; set; }
    [JsonProperty("profitandloss")]      public decimal ProfitAndLoss      { get; set; }
    [JsonProperty("pnlpercentage")]      public decimal Pnlpercentage      { get; set; }
}

public class AngelTradeDto
{
    [JsonProperty("orderid")]        public string  OrderId         { get; set; } = string.Empty;
    [JsonProperty("tradeid")]        public string  TradeId         { get; set; } = string.Empty;
    [JsonProperty("tradingsymbol")]  public string  TradingSymbol   { get; set; } = string.Empty;
    [JsonProperty("exchange")]       public string  Exchange        { get; set; } = string.Empty;
    [JsonProperty("transactiontype")]public string  TransactionType { get; set; } = string.Empty;
    [JsonProperty("quantity")]       public int     Quantity        { get; set; }
    [JsonProperty("tradeprice")]     public decimal TradePrice      { get; set; }
    [JsonProperty("filltime")]       public DateTime FillTime       { get; set; }
    [JsonProperty("producttype")]    public string  ProductType     { get; set; } = string.Empty;
}
