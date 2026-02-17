using Newtonsoft.Json;

namespace Cognexalgo.Core.Models
{
    public class RMSLimit
    {
        [JsonProperty("net")]
        public double Net { get; set; }

        [JsonProperty("availablecash")]
        public double AvailableCash { get; set; }

        [JsonProperty("availableintradaypayin")]
        public double AvailableIntradayPayin { get; set; }

        [JsonProperty("availablelimitmargin")]
        public double AvailableLimitMargin { get; set; }

        [JsonProperty("collateral")]
        public double Collateral { get; set; }

        [JsonProperty("m2munrealized")]
        public double M2MUnrealized { get; set; }

        [JsonProperty("m2mrealized")]
        public double M2MRealized { get; set; }

        [JsonProperty("utiliseddebits")]
        public double UtilisedDebits { get; set; }

        [JsonProperty("utilisedspan")]
        public double UtilisedSpan { get; set; }

        [JsonProperty("utilisedoptionpremium")]
        public double UtilisedOptionPremium { get; set; }
    }
}
