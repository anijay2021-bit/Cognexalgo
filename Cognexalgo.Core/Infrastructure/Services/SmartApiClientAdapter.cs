using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cognexalgo.Core.Application.Interfaces;
using Cognexalgo.Core.Domain.Entities;
using Cognexalgo.Core.Domain.Enums;
using Cognexalgo.Core.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Alias to disambiguate Domain.Entities.Order from Models.Order
using LegacyOrder = Cognexalgo.Core.Models.Order;

namespace Cognexalgo.Core.Infrastructure.Services
{
    /// <summary>
    /// Adapter wrapping the existing SmartApiClient to implement IAngelOneAdapter.
    /// Bridges the legacy API client with the new V2 interface.
    /// </summary>
    public class SmartApiClientAdapter : IAngelOneAdapter
    {
        private readonly SmartApiClient _client;
        private bool _isAuthenticated = false;

        public bool IsAuthenticated => _isAuthenticated;
        public event Action? OnSessionExpired;

        public SmartApiClientAdapter(SmartApiClient client)
        {
            _client = client;
            _client.OnSessionExpired += () => OnSessionExpired?.Invoke();
        }

        public SmartApiClientAdapter() : this(new SmartApiClient()) { }

        // ─── Auth ──────────────────────────────────────────────────

        public async Task<AuthResult> LoginAsync(string clientCode, string password,
                                                  string totp, string apiKey)
        {
            try
            {
                _client.SetApiKey(apiKey);
                bool success = await _client.LoginAsync(clientCode, password, totp);
                _isAuthenticated = success;

                return new AuthResult
                {
                    Success = success,
                    JwtToken = _client.JwtToken,
                    FeedToken = _client.FeedToken
                };
            }
            catch (Exception ex)
            {
                return new AuthResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        public Task<bool> RefreshTokenAsync()
        {
            _isAuthenticated = false;
            return Task.FromResult(false);
        }

        // ─── Market Data ──────────────────────────────────────────

        public Task<List<InstrumentMaster>> GetOptionChainAsync(string symbol, string expiry)
        {
            // Option chain built from InstrumentMaster DB, not direct API
            return Task.FromResult(new List<InstrumentMaster>());
        }

        public async Task<QuoteData> GetLTPAsync(string exchange, string symbol, string token)
        {
            try
            {
                // GetLTPDataAsync returns LTPResponse with .Data.Ltp
                var ltpResponse = await _client.GetLTPDataAsync(exchange, symbol, token);
                if (ltpResponse?.Data != null)
                {
                    return new QuoteData
                    {
                        Symbol = symbol,
                        Token = token,
                        Ltp = (decimal)ltpResponse.Data.Ltp
                        // LTPData only has Ltp field, not OHLC
                    };
                }
            }
            catch { }

            return new QuoteData { Symbol = symbol, Token = token };
        }

        public Task SubscribeWebSocketAsync(List<string> tokens, Action<TickData> onTick)
            => Task.CompletedTask; // Existing TickerService handles WebSocket separately

        public Task UnsubscribeWebSocketAsync(List<string> tokens)
            => Task.CompletedTask;

        public async Task<List<CandleData>> GetCandleDataAsync(
            string exchange, string symbol, string token,
            string interval, DateTime from, DateTime to)
        {
            try
            {
                // Legacy signature: GetHistoricalDataAsync(exchange, symbolToken, interval, from, to)
                // Does NOT take a `symbol` param — uses `token` as symboltoken
                var jArray = await _client.GetHistoricalDataAsync(exchange, token, interval, from, to);
                if (jArray != null)
                {
                    return jArray.Select(c =>
                    {
                        var arr = c as JArray;
                        if (arr == null || arr.Count < 6) return null;
                        return new CandleData
                        {
                            Timestamp = arr[0].ToObject<DateTime>(),
                            Open = arr[1].ToObject<decimal>(),
                            High = arr[2].ToObject<decimal>(),
                            Low = arr[3].ToObject<decimal>(),
                            Close = arr[4].ToObject<decimal>(),
                            Volume = arr[5].ToObject<long>()
                        };
                    }).Where(c => c != null).ToList()!;
                }
            }
            catch { }

            return new List<CandleData>();
        }

        // ─── Orders ──────────────────────────────────────────────

        public async Task<PlaceOrderResult> PlaceOrderAsync(AngelOrderRequest request)
        {
            try
            {
                var orderId = await _client.PlaceOrderAsync(
                    request.TradingSymbol,
                    request.SymbolToken,
                    request.TransactionType,
                    request.Quantity,
                    (double)request.Price,
                    request.Variety,
                    request.ProductType,
                    request.Exchange);

                return new PlaceOrderResult
                {
                    Success = !string.IsNullOrEmpty(orderId),
                    BrokerOrderId = orderId
                };
            }
            catch (Exception ex)
            {
                return new PlaceOrderResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        public Task<ModifyOrderResult> ModifyOrderAsync(string orderId, AngelModifyRequest request)
            => Task.FromResult(new ModifyOrderResult { Success = false, ErrorMessage = "Not yet implemented" });

        public Task<bool> CancelOrderAsync(string orderId, string variety)
            => Task.FromResult(false);

        public async Task<List<AngelOrderResponse>> GetOrderBookAsync()
        {
            try
            {
                // Returns List<Models.Order> (legacy Order model)
                var orders = await _client.GetOrderBookAsync();
                if (orders != null)
                {
                    return orders.Select(o => new AngelOrderResponse
                    {
                        OrderId = o.OrderId ?? "",
                        TradingSymbol = o.Symbol ?? "",
                        TransactionType = o.TransactionType ?? "",
                        Status = o.Status ?? "",
                        Quantity = (int)o.Qty,
                        Price = (decimal)o.Price
                    }).ToList();
                }
            }
            catch { }

            return new List<AngelOrderResponse>();
        }

        public Task<AngelOrderResponse?> GetOrderStatusAsync(string orderId)
            => Task.FromResult<AngelOrderResponse?>(null);

        // ─── Portfolio ──────────────────────────────────────────

        public async Task<List<AngelPositionResponse>> GetPositionsAsync()
        {
            try
            {
                var positions = await _client.GetPositionAsync();
                if (positions != null)
                {
                    return positions.Select(p => new AngelPositionResponse
                    {
                        TradingSymbol = p.TradingSymbol ?? "",
                        SymbolToken = p.SymbolToken ?? "",
                        Exchange = p.Exchange ?? "",
                        ProductType = p.ProductType ?? "",
                        NetQty = int.TryParse(p.NetQty, out int nq) ? nq : 0,
                        BuyAvgPrice = (decimal)p.BuyAvgPrice,
                        SellAvgPrice = (decimal)p.SellAvgPrice,
                        Ltp = (decimal)p.Ltp
                    }).ToList();
                }
            }
            catch { }

            return new List<AngelPositionResponse>();
        }

        public async Task<RmsLimitData> GetRMSLimitsAsync()
        {
            try
            {
                // RMSLimit fields are double, not string
                var rmsData = await _client.GetRMSLimitAsync();
                if (rmsData != null)
                {
                    return new RmsLimitData
                    {
                        AvailableMargin = (decimal)rmsData.AvailableCash,
                        UsedMargin = (decimal)rmsData.UtilisedDebits
                    };
                }
            }
            catch { }

            return new RmsLimitData();
        }

        public async Task<List<AngelHoldingResponse>> GetHoldingsAsync()
        {
            try
            {
                var holdings = await _client.GetHoldingsAsync();
                if (holdings != null)
                {
                    return holdings.Select(h => new AngelHoldingResponse
                    {
                        TradingSymbol = h.TradingSymbol ?? "",
                        Exchange = h.Exchange ?? "",
                        Quantity = (int)h.Quantity, // long → int cast
                        AveragePrice = (decimal)h.AveragePrice,
                        Ltp = (decimal)h.Ltp,
                        PnL = (decimal)h.ProfitAndLoss
                    }).ToList();
                }
            }
            catch { }

            return new List<AngelHoldingResponse>();
        }
    }
}
