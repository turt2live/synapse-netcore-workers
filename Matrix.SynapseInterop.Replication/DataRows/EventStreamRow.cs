using System.Collections.Generic;
using Newtonsoft.Json;

namespace Matrix.SynapseInterop.Replication.DataRows
{
    public class EventStreamRow : IReplicationDataRow
    {
        public string EventId { get; private set; }
        public string RoomId { get; private set; }
        public string EventType { get; private set; }
        public string StateKey { get; private set; } // nullable
        public string RedactsEventId { get; private set; } // nullable
        private EventStreamRow() { }

        public static EventStreamRow FromRaw(string rawDataString)
        {
            var parsed = JsonConvert.DeserializeObject<List<dynamic>>(rawDataString);

            return new EventStreamRow
            {
                EventId = parsed[0],
                RoomId = parsed[1],
                EventType = parsed[2],
                StateKey = parsed[3],
                RedactsEventId = parsed[4]
            };
        }
    }
}
