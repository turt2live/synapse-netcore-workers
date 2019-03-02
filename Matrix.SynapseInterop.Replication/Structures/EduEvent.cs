using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Matrix.SynapseInterop.Replication.Structures
{
    public struct EduEvent
    {
        public JObject content;
        public string edu_type;

        [JsonIgnore]
        public string InternalKey;

        [JsonIgnore]
        public long StreamId;

        [JsonIgnore]
        public string origin;

        [JsonIgnore]
        public string destination;
    }
}
