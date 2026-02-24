namespace Cognexalgo.Core.Models
{
    public class BrokerCredentials
    {
        public int Id { get; set; }
        public string BrokerName { get; set; } = "AngelOne";
        public string ApiKey { get; set; }
        public string ClientCode { get; set; }
        public string Password { get; set; }
        public string TotpKey { get; set; }
    }
}
