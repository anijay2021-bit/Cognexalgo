using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cognexalgo.Core.Domain.Entities;
using Cognexalgo.Core.Domain.Enums;

namespace Cognexalgo.Core.Application.Interfaces
{
    /// <summary>
    /// Full Angel One SmartAPI adapter interface per Module 5 spec.
    /// </summary>
    public interface IAngelOneAdapter
    {
        // ─── Auth ──────────────────────────────────────────────────
        Task<AuthResult> LoginAsync(string clientCode, string password,
                                     string totp, string apiKey);
        Task<bool> RefreshTokenAsync();
        bool IsAuthenticated { get; }

        event Action? OnSessionExpired;

        // ─── Market Data ──────────────────────────────────────────
        Task<List<InstrumentMaster>> GetOptionChainAsync(string symbol, string expiry);
        Task<QuoteData> GetLTPAsync(string exchange, string symbol, string token);
        Task SubscribeWebSocketAsync(List<string> tokens, Action<TickData> onTick);
        Task UnsubscribeWebSocketAsync(List<string> tokens);
        Task<List<CandleData>> GetCandleDataAsync(string exchange, string symbol,
                                                    string token, string interval,
                                                    DateTime from, DateTime to);

        // ─── Orders ──────────────────────────────────────────────
        Task<PlaceOrderResult> PlaceOrderAsync(AngelOrderRequest request);
        Task<ModifyOrderResult> ModifyOrderAsync(string orderId, AngelModifyRequest request);
        Task<bool> CancelOrderAsync(string orderId, string variety);
        Task<List<AngelOrderResponse>> GetOrderBookAsync();
        Task<AngelOrderResponse?> GetOrderStatusAsync(string orderId);

        // ─── Portfolio ──────────────────────────────────────────
        Task<List<AngelPositionResponse>> GetPositionsAsync();
        Task<RmsLimitData> GetRMSLimitsAsync();
        Task<List<AngelHoldingResponse>> GetHoldingsAsync();
    }

    // ─── DTOs ─────────────────────────────────────────────────────

    public class AuthResult
    {
        public bool Success { get; set; }
        public string? JwtToken { get; set; }
        public string? RefreshToken { get; set; }
        public string? FeedToken { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class QuoteData
    {
        public string Symbol { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public decimal Ltp { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public long Volume { get; set; }
    }

    public class TickData
    {
        public string Token { get; set; } = string.Empty;
        public decimal Ltp { get; set; }
        public long Volume { get; set; }
        public long OpenInterest { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class CandleData
    {
        public DateTime Timestamp { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public long Volume { get; set; }
    }

    public class AngelOrderRequest
    {
        public string TradingSymbol { get; set; } = string.Empty;
        public string SymbolToken { get; set; } = string.Empty;
        public string Exchange { get; set; } = "NFO";
        public string TransactionType { get; set; } = "BUY"; // BUY, SELL
        public string OrderType { get; set; } = "MARKET";     // MARKET, LIMIT, STOPLOSS, STOPLOSS_MARKET
        public string ProductType { get; set; } = "MIS";      // MIS, NRML, CNC
        public string Variety { get; set; } = "NORMAL";       // NORMAL, STOPLOSS, AMO
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal TriggerPrice { get; set; }
        public string? Duration { get; set; } = "DAY";
    }

    public class PlaceOrderResult
    {
        public bool Success { get; set; }
        public string? BrokerOrderId { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Script { get; set; }
    }

    public class AngelModifyRequest
    {
        public string? OrderType { get; set; }
        public int? Quantity { get; set; }
        public decimal? Price { get; set; }
        public decimal? TriggerPrice { get; set; }
    }

    public class ModifyOrderResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class AngelOrderResponse
    {
        public string OrderId { get; set; } = string.Empty;
        public string TradingSymbol { get; set; } = string.Empty;
        public string TransactionType { get; set; } = string.Empty;
        public string OrderType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal AveragePrice { get; set; }
        public int FilledShares { get; set; }
        public int UnfilledShares { get; set; }
        public string? Text { get; set; } // Rejection reason
        public string? UpdateTime { get; set; }
    }

    public class AngelPositionResponse
    {
        public string TradingSymbol { get; set; } = string.Empty;
        public string SymbolToken { get; set; } = string.Empty;
        public string Exchange { get; set; } = string.Empty;
        public string ProductType { get; set; } = string.Empty;
        public int NetQty { get; set; }
        public decimal BuyAvgPrice { get; set; }
        public decimal SellAvgPrice { get; set; }
        public decimal Ltp { get; set; }
        public decimal RealizedProfit { get; set; }
        public decimal UnrealizedProfit { get; set; }
    }

    public class RmsLimitData
    {
        public decimal AvailableMargin { get; set; }
        public decimal UsedMargin { get; set; }
        public decimal TotalMargin => AvailableMargin + UsedMargin;
        public decimal MarginUtilizationPercent => TotalMargin > 0
            ? (UsedMargin / TotalMargin) * 100 : 0;
    }

    public class AngelHoldingResponse
    {
        public string TradingSymbol { get; set; } = string.Empty;
        public string Exchange { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal AveragePrice { get; set; }
        public decimal Ltp { get; set; }
        public decimal PnL { get; set; }
    }
}
