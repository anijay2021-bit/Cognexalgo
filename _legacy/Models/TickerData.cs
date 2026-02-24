using System.Collections.Generic;
using Newtonsoft.Json;

namespace Cognexalgo.Core.Models
{
    public class TickerPayload
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("data")]
        public TickerData Data { get; set; }
    }

    public class TickerData
    {
        [JsonProperty("NIFTY")]
        public InstrumentInfo Nifty { get; set; }

        [JsonProperty("BANKNIFTY")]
        public InstrumentInfo BankNifty { get; set; }

        [JsonProperty("FINNIFTY")]
        public InstrumentInfo FinNifty { get; set; }

        [JsonProperty("MIDCPNIFTY")]
        public InstrumentInfo MidcpNifty { get; set; }

        [JsonProperty("SENSEX")]
        public InstrumentInfo Sensex { get; set; }

        [JsonProperty("OPTIONS")]
        public Dictionary<string, InstrumentInfo> Options { get; set; } = new Dictionary<string, InstrumentInfo>();
    }

    public class InstrumentInfo
    {
        [JsonProperty("token")]
        public string Token { get; set; }

        [JsonProperty("symbol")]
        public string Symbol { get; set; }

        [JsonProperty("ltp")]
        public double Ltp { get; set; }

        [JsonProperty("change")]
        public double Change { get; set; }

        [JsonProperty("change_percent")]
        public double ChangePercent { get; set; }

        [JsonProperty("open")]
        public double Open { get; set; }

        [JsonProperty("high")]
        public double High { get; set; }

        [JsonProperty("low")]
        public double Low { get; set; }

        [JsonProperty("close")]
        public double Close { get; set; }
    }
}
