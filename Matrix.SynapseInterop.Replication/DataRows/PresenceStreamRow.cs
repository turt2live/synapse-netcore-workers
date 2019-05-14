using Newtonsoft.Json;

namespace Matrix.SynapseInterop.Replication.DataRows
{
    public class PresenceStreamRow: IReplicationDataRow
    {
        public string UserId { get; set; }
        public string State { get; set; }
        public long LastActiveTs { get; set; }
        public long LastFederationUpdateTs { get; set; }
        public long LastUserSyncTs { get; set; }
        public string StatusMsg { get; set; }
        public bool CurrentlyActive { get; set; }
        
        public static PresenceStreamRow FromRaw(string rawDataString)
        {
            var parsed = JsonConvert.DeserializeObject<dynamic>(rawDataString);

            return new PresenceStreamRow
            {
                UserId = parsed[0],
                State = parsed[1],
                LastActiveTs = parsed[2],
                LastFederationUpdateTs = parsed[3],
                LastUserSyncTs = parsed[4],
                StatusMsg = parsed[5],
                CurrentlyActive = parsed[6]
            };
        }
    }
}
