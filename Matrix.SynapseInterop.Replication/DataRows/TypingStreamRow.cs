using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Matrix.SynapseInterop.Replication.DataRows
{
    public class TypingStreamRow : IReplicationDataRow
    {
        public string[] UserIds { get; private set; }
        public string RoomId { get; private set; }
        private TypingStreamRow() { }

        public static TypingStreamRow FromRaw(string rawDataString)
        {
            var parsed = JsonConvert.DeserializeObject<List<dynamic>>(rawDataString);

            return new TypingStreamRow
            {
                RoomId = parsed[0],
                UserIds = (parsed[1] as JArray)?.ToObject<string[]>()
            };
        }
    }
}
