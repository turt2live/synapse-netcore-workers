using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Matrix.SynapseInterop.Replication.DataRows
{
    public class ReceiptStreamRow : IReplicationDataRow
    {
        public string EventId { get; private set; }
        public string RoomId { get; private set; }
        public string UserId { get; private set; }
        public JObject Data { get; private set; }
        public string RecieptType { get; private set; }
        private ReceiptStreamRow() { }

        public static ReceiptStreamRow FromRaw(string rawDataString)
        {
            var parsed = JsonConvert.DeserializeObject<List<dynamic>>(rawDataString);

            return new ReceiptStreamRow
            {
                RoomId = parsed[0],
                RecieptType = parsed[1],
                UserId = parsed[2],
                EventId = parsed[3],
                Data = parsed[4] as JObject,
            };
        }
    }
}
