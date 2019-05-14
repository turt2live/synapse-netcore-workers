using System.Collections.Generic;
using Newtonsoft.Json;

namespace Matrix.SynapseInterop.Replication.DataRows
{
    public class ToDeviceStreamRow: IReplicationDataRow
    {
        public string Entity { get; set; }
        
        public static ToDeviceStreamRow FromRaw(string rawDataString)
        {
            var parsed = JsonConvert.DeserializeObject<dynamic>(rawDataString);

            return new ToDeviceStreamRow()
            {
                Entity = parsed[0]
            };
        }
    }
}