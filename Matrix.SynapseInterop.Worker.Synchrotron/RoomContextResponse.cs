using System.Collections.Generic;
using Newtonsoft.Json;

namespace Matrix.SynapseInterop.Worker.Synchrotron
{
    public class RoomContextResponse
    {
        [JsonProperty("start")]
        public string Start;

        [JsonProperty("end")]
        public string End;
        
        [JsonProperty("event")]
        public SyncResponse.SyncStateEvent Event;

        [JsonProperty("events_before")]
        public List<SyncResponse.SyncStateEvent> EventsBefore;

        [JsonProperty("events_after")]
        public List<SyncResponse.SyncStateEvent> EventsAfter;

        [JsonProperty("state")]
        public List<SyncResponse.SyncStateEvent> State;
    }
}
