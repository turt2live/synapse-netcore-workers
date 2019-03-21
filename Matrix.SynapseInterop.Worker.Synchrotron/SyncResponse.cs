using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Matrix.SynapseInterop.Worker.Synchrotron
{
    public class SyncResponse
    {
        [JsonProperty(PropertyName = "next_batch")]
        public string NextBatch;

        [JsonProperty(PropertyName = "rooms")]
        public SyncRoomSet Rooms;

        public class SyncRoomSet
        {
            [JsonProperty(PropertyName = "join")]
            public Dictionary<string, JoinedRoom> Join;
            
            [JsonProperty(PropertyName = "invite")]
            public Dictionary<string, InvitedRoom> Invite;
            
            [JsonProperty(PropertyName = "leave")]
            public Dictionary<string, LeftRoom> Leave;
        }
        
        public interface ISyncRoom
        {
            
        }

        public class JoinedRoom : ISyncRoom
        {
            
        }

        public class InvitedRoom
        {
            
        }

        public class LeftRoom : ISyncRoom
        {
            
        }
    }
}
