using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Matrix.SynapseInterop.Replication.Structures
{
    public struct EduEvent
    {
        [JsonIgnore]
        public long StreamId;
        public JObject content;
        public string origin;
        public string destination;
        public string edu_type;
    }
}