using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace Cognexalgo.Core.Models
{
    public class Holding
    {
        [JsonProperty("tradingsymbol")]
        public string TradingSymbol { get; set; }

        [JsonProperty("symboltoken")]
        public string SymbolToken { get; set; }

        [JsonProperty("exchange")]
        public string Exchange { get; set; }

        [JsonProperty("isin")]
        public string Isin { get; set; }

        [JsonProperty("quantity")]
        public long Quantity { get; set; }

        [JsonProperty("authorisedquantity")]
        public long AuthorisedQuantity { get; set; }

        [JsonProperty("averageprice")]
        public double AveragePrice { get; set; }

        [JsonProperty("ltp")]
        public double Ltp { get; set; }

        [JsonProperty("close")]
        public double Close { get; set; }

        [JsonProperty("profitandloss")]
        public double ProfitAndLoss { get; set; }

        [JsonProperty("pnlpercentage")]
        public double PnlPercentage { get; set; }
    }

    /// <summary>
    /// Live broker position. Extends ObservableObject so that Ltp and Pnl updates
    /// (driven by SmartStream ticks in MainViewModel.OnTick) propagate to WPF DataGrid
    /// bindings without needing a full collection refresh.
    /// </summary>
    public partial class Position : ObservableObject
    {
        [JsonProperty("tradingsymbol")]
        public string TradingSymbol { get; set; }

        [JsonProperty("symboltoken")]
        public string SymbolToken { get; set; }

        [JsonProperty("exchange")]
        public string Exchange { get; set; }

        [JsonProperty("instrumenttype")]
        public string InstrumentType { get; set; }

        [JsonProperty("producttype")]
        public string ProductType { get; set; }

        [JsonProperty("optiontype")]
        public string OptionType { get; set; }

        [JsonProperty("token")]
        public string Token { get; set; }

        [JsonProperty("buyqty")]
        public string BuyQty { get; set; }

        [JsonProperty("sellqty")]
        public string SellQty { get; set; }

        [JsonProperty("buyamount")]
        public double BuyAmount { get; set; }

        [JsonProperty("sellamount")]
        public double SellAmount { get; set; }

        [JsonProperty("cfbuyqty")]
        public string CfBuyQty { get; set; }

        [JsonProperty("cfsellqty")]
        public string CfSellQty { get; set; }

        [JsonProperty("netqty")]
        public string NetQty { get; set; }

        [JsonProperty("buyavgprice")]
        public double BuyAvgPrice { get; set; }

        [JsonProperty("sellavgprice")]
        public double SellAvgPrice { get; set; }

        [JsonProperty("avgnetprice")]
        public double AvgNetPrice { get; set; }

        [JsonProperty("netvalue")]
        public double NetValue { get; set; }

        [JsonProperty("netprice")]
        public double NetPrice { get; set; }

        // ── Observable for live tick updates ──────────────────────────────────
        // SetProperty fires INotifyPropertyChanged so WPF DataGrid cells refresh
        // without rebuilding the entire collection.

        private double _pnl;
        [JsonProperty("pnl")]
        public double Pnl { get => _pnl; set => SetProperty(ref _pnl, value); }

        private double _ltp;
        [JsonProperty("ltp")]
        public double Ltp { get => _ltp; set => SetProperty(ref _ltp, value); }

        [JsonProperty("closedpositions")]
        public string ClosedPositions { get; set; }

        // Additional tracking properties (not from API)
        public string Status { get; set; } = "Running";
        public double EntryPrice { get; set; }
        public double StopLoss { get; set; }
        public double Target { get; set; }
    }
}
