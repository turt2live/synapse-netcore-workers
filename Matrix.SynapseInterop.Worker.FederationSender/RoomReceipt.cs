using System.Collections.Generic;
using Newtonsoft.Json;
namespace Matrix.SynapseInterop.Worker.FederationSender
{
    public struct RoomReceipt
    {
        [JsonProperty("m.read")]
        public Dictionary<string, UserReadReceipt> MRead;
    }

    public struct UserReadReceipt
    {
        public string[] event_ids;
        public RoomReceiptMetadata data;
    }

    public struct RoomReceiptMetadata
    {
        public long ts;
    }
}
