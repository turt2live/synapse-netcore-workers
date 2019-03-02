using System.Collections.Generic;
using Matrix.SynapseInterop.Replication.Structures;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Matrix.SynapseInterop.Replication.DataRows
{
    public class FederationStreamRow : IReplicationDataRow
    {
        public List<string> devices;
        public List<EduEvent> edus;
        public Dictionary<string[], EduEvent> keyedEdus;
        public List<PresenceState> presence;

        private FederationStreamRow()
        {
            keyedEdus = new Dictionary<string[], EduEvent>();
            edus = new List<EduEvent>();
            devices = new List<string>();
            presence = new List<PresenceState>();
        }

        public static FederationStreamRow FromRaw(string rawDataString)
        {
            var parsed = JsonConvert.DeserializeObject<List<dynamic>>(rawDataString);
            var typeId = "";
            var streamRow = new FederationStreamRow();

            for (var i = 0; i < parsed.Count; i++)
                if (i == 0)
                {
                    typeId = parsed[0];
                }
                else if (typeId == "k") // KeyedEduRow
                {
                    var edu = new EduEvent
                    {
                        edu_type = parsed[i].edu.edu_type,
                        destination = parsed[i].edu.destination,
                        origin = parsed[i].edu.origin,
                        content = parsed[i].edu.content
                    };

                    string[] key = parsed[i].key.ToObject<string[]>();
                    streamRow.keyedEdus.Add(key, edu);
                }
                else if (typeId == "p") // PresenceRow
                {
                    streamRow.presence.Add(((JObject) parsed[i]).ToObject<PresenceState>());
                }
                else if (typeId == "e") // EduRow
                {
                    streamRow.edus.Add(new EduEvent
                    {
                        edu_type = parsed[i].edu_type,
                        destination = parsed[i].destination,
                        origin = parsed[i].origin,
                        content = parsed[i].content
                    });
                }
                else if (typeId == "d") // DeviceRow
                {
                    string destination = parsed[i]["destination"].Value;
                    streamRow.devices.Add(destination);
                }

            return streamRow;
        }
    }
}
