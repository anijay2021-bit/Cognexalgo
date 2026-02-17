using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Cognexalgo.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cognexalgo.Core.Services
{
    public class SmartApiClient
    {
        public event Action OnSessionExpired;

        private HttpClient _client;

        private void CheckAuth(HttpResponseMessage response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                OnSessionExpired?.Invoke();
            }
        }

        // ... LoginAsync ...

        // (Similar for GetHoldings and PlaceOrder if needed, but GetPosition is the main poll)
        public string JwtToken { get; private set; }
        private string _apiKey;
        private string _clientCode;
        public string FeedToken { get; private set; }

        private const string BASE_URL = "https://apiconnect.angelbroking.com";

        public SmartApiClient(string apiKey)
        {
            InitializeClient(apiKey);
        }

        public SmartApiClient()
        {
            _client = new HttpClient { BaseAddress = new Uri(BASE_URL) };
            _client.DefaultRequestHeaders.Add("X-User-Type", "USER");
            _client.DefaultRequestHeaders.Add("X-SourceID", "WEB");
            _client.DefaultRequestHeaders.Add("X-ClientLocalIP", "127.0.0.1");
            _client.DefaultRequestHeaders.Add("X-ClientPublicIP", "127.0.0.1");
            _client.DefaultRequestHeaders.Add("X-MACAddress", "MAC_ADDRESS");
        }

        public void SetApiKey(string apiKey)
        {
            _apiKey = apiKey;
            if (_client.DefaultRequestHeaders.Contains("X-PrivateKey"))
            {
                _client.DefaultRequestHeaders.Remove("X-PrivateKey");
            }
            _client.DefaultRequestHeaders.Add("X-PrivateKey", apiKey);
        }

        private void InitializeClient(string apiKey)
        {
             _apiKey = apiKey;
            _client = new HttpClient { BaseAddress = new Uri(BASE_URL) };
            _client.DefaultRequestHeaders.Add("X-User-Type", "USER");
            _client.DefaultRequestHeaders.Add("X-SourceID", "WEB");
            _client.DefaultRequestHeaders.Add("X-ClientLocalIP", "127.0.0.1");
            _client.DefaultRequestHeaders.Add("X-ClientPublicIP", "127.0.0.1");
            _client.DefaultRequestHeaders.Add("X-MACAddress", "MAC_ADDRESS");
            _client.DefaultRequestHeaders.Add("X-PrivateKey", apiKey); 
        }

        public async Task<bool> LoginAsync(string clientCode, string password, string totp)
        {
            _clientCode = clientCode;
            
            var payload = new
            {
                clientcode = clientCode,
                password = password,
                totp = totp
            };

            var json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try 
            {
                var response = await _client.PostAsync("/rest/auth/angelbroking/user/v1/loginByPassword", content);
                var responseString = await response.Content.ReadAsStringAsync();
                
                // Check if response is empty or not JSON
                if (string.IsNullOrWhiteSpace(responseString))
                {
                    throw new Exception("API returned empty response. Check your internet connection or API endpoint.");
                }

                // Try to parse as JSON
                JObject data;
                try
                {
                    data = JObject.Parse(responseString);
                }
                catch (JsonException)
                {
                    // Response is not JSON - show first 200 chars of actual response
                    string preview = responseString.Length > 200 
                        ? responseString.Substring(0, 200) + "..." 
                        : responseString;
                    throw new Exception($"API returned non-JSON response. This usually means:\n" +
                        $"1. Invalid API endpoint\n" +
                        $"2. API is down\n" +
                        $"3. Network/firewall blocking the request\n\n" +
                        $"Response preview: {preview}");
                }
                
                if (data["status"]?.Value<bool>() == true)
                {
                    JwtToken = data["data"]["jwtToken"]?.ToString();
                    FeedToken = data["data"]["feedToken"]?.ToString();
                    
                    // Set Auth Header for future requests
                    _client.DefaultRequestHeaders.Authorization = 
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", JwtToken);
                        
                    return true;
                }
                else
                {
                    string msg = data["message"]?.ToString() ?? "Unknown API Error";
                    string code = data["errorcode"]?.ToString() ?? "N/A";
                    throw new Exception($"{msg} (Code: {code})");
                }
            }
            catch(Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Login Error: {ex.Message}");
                throw; // Rethrow to let UI see the error
            }
        }

        public async Task<string> PlaceOrderAsync(string symbol, string token, string transactionType, int qty, double price, string variety = "NORMAL", string productType = "MIS", string exchange = "NFO")
        {
            var payload = new
            {
                variety = variety,
                tradingsymbol = symbol,
                symboltoken = token,
                transactiontype = transactionType,
                exchange = exchange,
                ordertype = price > 0 ? "LIMIT" : "MARKET",
                producttype = productType,
                duration = "DAY",
                price = price,
                quantity = qty,
                squareoff = "0",
                stoploss = "0"
            };

            var json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _client.PostAsync("/rest/secure/angelbroking/order/v1/placeOrder", content);
            var responseString = await response.Content.ReadAsStringAsync();
            
            var data = JObject.Parse(responseString);
             if (data["status"]?.Value<bool>() == true)
            {
                return data["data"]["orderid"].ToString();
            }
            return null; // Or throw ex
        }
        
        public async Task<System.Collections.Generic.List<Holding>> GetHoldingsAsync()
        {
            try
            {
                var response = await _client.GetAsync("/rest/secure/angelbroking/portfolio/v1/getHoldings");
                CheckAuth(response); 
                var responseString = await response.Content.ReadAsStringAsync();
                
                var data = JObject.Parse(responseString);
                if (data["status"]?.Value<bool>() == true && data["data"] != null)
                {
                    return data["data"].ToObject<System.Collections.Generic.List<Holding>>();
                }
                return new System.Collections.Generic.List<Holding>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetHoldings Error: {ex.Message}");
                return new System.Collections.Generic.List<Holding>();
            }
        }

        public async Task<System.Collections.Generic.List<Position>> GetPositionAsync()
        {
            try
            {
                var response = await _client.GetAsync("/rest/secure/angelbroking/order/v1/getPosition");
                CheckAuth(response);
                var responseString = await response.Content.ReadAsStringAsync();
                
                var data = JObject.Parse(responseString);
                if (data["status"]?.Value<bool>() == true && data["data"] != null)
                {
                    return data["data"].ToObject<System.Collections.Generic.List<Position>>();
                }
                return new System.Collections.Generic.List<Position>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetPosition Error: {ex.Message}");
                return new System.Collections.Generic.List<Position>();
            }
        }

        public async Task<System.Collections.Generic.List<Order>> GetOrderBookAsync()
        {
            try
            {
                var response = await _client.GetAsync("/rest/secure/angelbroking/order/v1/getOrderBook");
                CheckAuth(response);
                var responseString = await response.Content.ReadAsStringAsync();
                
                var data = JObject.Parse(responseString);
                if (data["status"]?.Value<bool>() == true && data["data"] != null)
                {
                    return data["data"].ToObject<System.Collections.Generic.List<Order>>();
                }
                return new System.Collections.Generic.List<Order>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetOrderBook Error: {ex.Message}");
                return new System.Collections.Generic.List<Order>();
            }
        }

        public async Task<RMSLimit> GetRMSLimitAsync()
        {
            try
            {
                var response = await _client.GetAsync("/rest/secure/angelbroking/user/v1/getRMS");
                CheckAuth(response);
                var responseString = await response.Content.ReadAsStringAsync();
                
                var data = JObject.Parse(responseString);
                if (data["status"]?.Value<bool>() == true && data["data"] != null)
                {
                    return data["data"].ToObject<RMSLimit>();
                }
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetRMSLimit Error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Fetches Last Traded Price (LTP) for a given instrument
        /// Used for real-time spot price and option premium fetching
        /// </summary>
        public async Task<LTPResponse> GetLTPDataAsync(string exchange, string tradingSymbol, string symbolToken)
        {
            try
            {
                var payload = new
                {
                    exchange = exchange,
                    tradingsymbol = tradingSymbol,
                    symboltoken = symbolToken
                };

                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _client.PostAsync("/rest/secure/angelbroking/order/v1/getLtpData", content);
                CheckAuth(response);
                
                var responseString = await response.Content.ReadAsStringAsync();
                var ltpResponse = JsonConvert.DeserializeObject<LTPResponse>(responseString);

                return ltpResponse;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetLTPData Error for {tradingSymbol}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get LTP data for multiple instruments in a single batch request (up to 50 tokens)
        /// </summary>
        /// <param name="exchange">Exchange (e.g., "NFO")</param>
        /// <param name="tokens">List of symbol tokens (max 50)</param>
        /// <returns>Dictionary mapping token to LTP, or null on error</returns>
        public async Task<System.Collections.Generic.Dictionary<string, double>> GetMarketDataBatchAsync(
            string exchange, 
            System.Collections.Generic.List<string> tokens)
        {
            try
            {
                if (tokens == null || tokens.Count == 0)
                    return new System.Collections.Generic.Dictionary<string, double>();

                if (tokens.Count > 50)
                    throw new ArgumentException("Maximum 50 tokens allowed per batch request");

                // Angel One batch market data API endpoint
                var payload = new
                {
                    mode = "FULL", // FULL mode includes LTP
                    exchangeTokens = new
                    {
                        NFO = tokens // Can also support NSE, BSE, etc.
                    }
                };

                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _client.PostAsync("/rest/secure/angelbroking/market/v1/quote/", content);
                CheckAuth(response);
                
                var responseString = await response.Content.ReadAsStringAsync();
                var batchResponse = JsonConvert.DeserializeObject<MarketDataBatchResponse>(responseString);

                if (batchResponse?.Status != true || batchResponse.Data?.FetchedData == null)
                {
                    System.Diagnostics.Debug.WriteLine($"Batch market data request failed: {batchResponse?.Message}");
                    return new System.Collections.Generic.Dictionary<string, double>();
                }

                // Map token -> LTP
                var result = new System.Collections.Generic.Dictionary<string, double>();
                foreach (var item in batchResponse.Data.FetchedData)
                {
                    if (item?.SymbolToken != null && item.Ltp.HasValue)
                    {
                        result[item.SymbolToken] = item.Ltp.Value;
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetMarketDataBatch Error: {ex.Message}");
                return new System.Collections.Generic.Dictionary<string, double>();
            }
        }

        /// <summary>
        /// Fetches historical candle data
        /// </summary>
        public async Task<JArray> GetHistoricalDataAsync(string exchange, string symbolToken, string interval, DateTime fromDate, DateTime toDate)
        {
            try
            {
                var payload = new
                {
                    exchange = exchange,
                    symboltoken = symbolToken,
                    interval = interval,
                    fromdate = fromDate.ToString("yyyy-MM-dd HH:mm"),
                    todate = toDate.ToString("yyyy-MM-dd HH:mm")
                };

                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _client.PostAsync("/rest/secure/angelbroking/historical/v1/getCandleData", content);
                CheckAuth(response);
                
                var responseString = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(responseString);

                if (data["status"]?.Value<bool>() == true && data["data"] != null)
                {
                    return data["data"] as JArray;
                }
                
                return new JArray();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetHistoricalData Error: {ex.Message}");
                return new JArray();
            }
        }
    }

    #region LTP Response Models

    public class LTPResponse
    {
        [JsonProperty("status")]
        public bool Status { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("data")]
        public LTPData Data { get; set; }
    }

    public class LTPData
    {
        [JsonProperty("exchange")]
        public string Exchange { get; set; }

        [JsonProperty("tradingsymbol")]
        public string TradingSymbol { get; set; }

        [JsonProperty("symboltoken")]
        public string SymbolToken { get; set; }

        [JsonProperty("ltp")]
        public double Ltp { get; set; }
    }

    #endregion

    #region Batch Market Data Response Models

    public class MarketDataBatchResponse
    {
        [JsonProperty("status")]
        public bool Status { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("data")]
        public MarketDataBatchData Data { get; set; }
    }

    public class MarketDataBatchData
    {
        [JsonProperty("fetched")]
        public System.Collections.Generic.List<MarketDataItem> FetchedData { get; set; }
    }

    public class MarketDataItem
    {
        [JsonProperty("symboltoken")]
        public string SymbolToken { get; set; }

        [JsonProperty("ltp")]
        public double? Ltp { get; set; }

        [JsonProperty("open")]
        public double? Open { get; set; }

        [JsonProperty("high")]
        public double? High { get; set; }

        [JsonProperty("low")]
        public double? Low { get; set; }

        [JsonProperty("close")]
        public double? Close { get; set; }
    }

    #endregion
}
