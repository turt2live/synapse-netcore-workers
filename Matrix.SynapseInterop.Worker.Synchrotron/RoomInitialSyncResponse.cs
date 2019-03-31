using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Matrix.SynapseInterop.Worker.Synchrotron
{
    public class RoomInitialSyncResponse
    {
        [JsonProperty("room_id")]
        public string RoomId;

        [JsonProperty("membership")]
        public string Membership;

        [JsonProperty("visibility")]
        public string Visibility;

        [JsonProperty("account_data")]
        public List<SyncResponse.SyncSimpleEvent> AccountData;

        [JsonProperty("messages")]
        public List<PaginationChunk> Messages;

        [JsonProperty("state")]
        public List<SyncResponse.SyncStateEvent> State;

        public class PaginationChunk
        {
            [JsonProperty("start", NullValueHandling = NullValueHandling.Ignore)]
            public string Start;

            [JsonProperty("end", NullValueHandling = NullValueHandling.Ignore)]
            public string End;

            [JsonProperty("chunk")]
            public List<SyncResponse.SyncRoomEvent> Chunk;
        }
    }
}
