using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Matrix.SynapseInterop.Replication.DataRows
{
    public class AccountDataStreamRow : IReplicationDataRow
    {
        public string UserId { get; private set; }
        public string RoomId { get; private set; }
        public string DataType { get; private set; }
        public JObject Data { get; private set; }
        
        public static AccountDataStreamRow FromRaw(string rawDataString)
        {
            var parsed = JsonConvert.DeserializeObject<List<dynamic>>(rawDataString);

            return new AccountDataStreamRow
            {
                UserId = parsed[0],
                RoomId = parsed[1],
                DataType = parsed[2],
                Data = JObject.Parse(parsed[3]),
            };
        }
    }
}
