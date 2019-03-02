using Matrix.SynapseInterop.Database.Models;
using Newtonsoft.Json.Linq;

namespace Matrix.SynapseInterop.Database
{
    public class EventJsonSet : Event
    {
        public JObject Content { get; }
        public int Version { get; }

        public EventJsonSet(Event baseEvent, string json, int version)
        {
            EventId = baseEvent.EventId;
            RoomId = baseEvent.RoomId;
            StreamOrdering = baseEvent.StreamOrdering;
            Sender = baseEvent.Sender;
            Content = JObject.Parse(json);
            Version = version;
        }
    }
}
