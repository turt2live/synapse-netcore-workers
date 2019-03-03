using Matrix.SynapseInterop.Database.WorkerModels;
using Newtonsoft.Json;

namespace Matrix.SynapseInterop.Worker.AppserviceSender.Dto
{
    internal class AppserviceDto
    {
        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        [JsonProperty("as_token")]
        public string AsToken { get; set; }

        [JsonProperty("hs_token")]
        public string HsToken { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("sender_localpart")]
        public string SenderLocalpart { get; set; }

        public AppserviceDto() { }

        public AppserviceDto(Appservice appservice)
        {
            Enabled = appservice.Enabled;
            AsToken = appservice.AppserviceToken;
            HsToken = appservice.HomeserverToken;
            Url = appservice.Url;
            SenderLocalpart = appservice.SenderLocalpart;
        }
    }
}
