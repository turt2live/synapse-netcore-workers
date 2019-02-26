using System.Collections.Generic;
using System.Security;
using Matrix.SynapseInterop.Replication.Structures;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Matrix.SynapseInterop.Worker.FederationSender
{
    public struct Transaction
    {
        public string transaction_id;
        public string origin;
        public string destination;
        public long origin_server_ts;
        [JsonProperty( NullValueHandling = NullValueHandling.Ignore )]
        public string[] previous_ids; // Not required.
        public List<EduEvent> edus;
        public List<IPduEvent> pdus; // Not required.
    }

    public interface IPduEvent
    {
        string room_id { get; set; }
        string sender { get; set; }
        string origin { get; set; }
        long origin_server_ts { get; set; }
        string type { get; set; }
        string state_key { get; set; }
        string redacts { get; set; }
        JObject unsigned { get; set; }
        JObject content { get; set; }
        JToken prev_events { get; set; }
        JToken auth_events { get; set; }
        long depth { get; set; }
        JToken hashes { get; set; }
        JToken signatures { get; set; }
    }
    
    public class PduEventV1 : IPduEvent
    {
        public string event_id { get; set; }
        public string room_id { get; set; }
        public string sender { get; set; }
        public string origin { get; set; }
        public long origin_server_ts { get; set; }
        public string type { get; set; }
        public string state_key { get; set; }
        public string redacts { get; set; }
        public JObject unsigned { get; set; }
        public JObject content { get; set; }
        public JToken prev_events { get; set; }
        public JToken auth_events { get; set; }
        public long depth { get; set; }
        public JToken hashes { get; set; }
        public JToken signatures { get; set; }
    }

    public class PduEventV3 : IPduEvent
    {
        public string room_id { get; set; }
        public string sender { get; set; }
        public string origin { get; set; }
        public long origin_server_ts { get; set; }
        public string type { get; set; }
        public string state_key { get; set; }
        public string redacts { get; set; }
        public JObject unsigned { get; set; }
        public JToken prev_events { get; set; }
        public JToken auth_events { get; set; }
        public long depth { get; set; }
        public JToken hashes { get; set; }
        public JToken signatures { get; set; }
        public JObject content { get; set; }
    }

    public struct PduEventHash
    {
        public string sha256;
    }
}