using System.Collections.Generic;
using System.Linq;
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

        [JsonProperty("namespaces")]
        public AppserviceNamespaceBagDto Namespaces { get; set; }

        public AppserviceDto() { }

        public AppserviceDto(Appservice appservice)
        {
            Enabled = appservice.Enabled;
            AsToken = appservice.AppserviceToken;
            HsToken = appservice.HomeserverToken;
            Url = appservice.Url;
            SenderLocalpart = appservice.SenderLocalpart;
            Namespaces = new AppserviceNamespaceBagDto(appservice.Namespaces);
        }
    }

    internal class AppserviceNamespaceBagDto
    {
        [JsonProperty("users")]
        public AppserviceNamespaceDto[] Users { get; set; }

        [JsonProperty("rooms")]
        public AppserviceNamespaceDto[] Rooms { get; set; }

        [JsonProperty("aliases")]
        public AppserviceNamespaceDto[] Aliases { get; set; }

        public AppserviceNamespaceBagDto() { }

        public AppserviceNamespaceBagDto(IEnumerable<AppserviceNamespace> namespaces)
        {
            if (namespaces == null) namespaces = new AppserviceNamespace[0];
            var appserviceNamespaces = namespaces as AppserviceNamespace[] ?? namespaces.ToArray();

            Users = appserviceNamespaces.Where(n => n.Kind == AppserviceNamespace.NS_USERS)
                                        .Select(n => new AppserviceNamespaceDto(n)).ToArray();

            Rooms = appserviceNamespaces.Where(n => n.Kind == AppserviceNamespace.NS_ROOMS)
                                        .Select(n => new AppserviceNamespaceDto(n)).ToArray();

            Aliases = appserviceNamespaces.Where(n => n.Kind == AppserviceNamespace.NS_ALIASES)
                                          .Select(n => new AppserviceNamespaceDto(n)).ToArray();
        }
    }

    internal class AppserviceNamespaceDto
    {
        [JsonProperty("exclusive")]
        public bool Exclusive { get; set; }

        [JsonProperty("regex")]
        public string Regex { get; set; }

        public AppserviceNamespaceDto() { }

        public AppserviceNamespaceDto(AppserviceNamespace ns)
        {
            Exclusive = ns.Exclusive;
            Regex = ns.Regex;
        }
    }
}
