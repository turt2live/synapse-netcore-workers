using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Matrix.SynapseInterop.Database
{
    public struct DeviceContentSet
    {
        public bool deleted { get; set; }
        [JsonProperty( NullValueHandling = NullValueHandling.Ignore )]
        public string device_display_name { get; set; }
        public string device_id { get; set; }
        public JObject keys { get; set; }
        public int[] prev_id { get; set; }
        public int stream_id { get; set; }
        public string user_id { get; set; }
    }
}