using Newtonsoft.Json;

namespace Matrix.SynapseInterop.Replication.DataRows
{
    public class DeviceListsStreamRow: IReplicationDataRow
    {
        public string UserId { get; set; }
        
        public string Destination { get; set; }
        
        public static DeviceListsStreamRow FromRaw(string rawDataString)
        {
            var parsed = JsonConvert.DeserializeObject<dynamic>(rawDataString);

            return new DeviceListsStreamRow()
            {
                UserId = parsed[0],
                Destination = parsed[1]
            };
        }
    }
}
