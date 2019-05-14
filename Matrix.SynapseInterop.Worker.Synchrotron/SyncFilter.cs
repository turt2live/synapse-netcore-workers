using System.Buffers.Text;
using Matrix.SynapseInterop.Database.SynapseModels;

namespace Matrix.SynapseInterop.Worker.Synchrotron
{
    public class SyncFilter
    {
        public int EventsToFetch = 10;

        public bool FullState { get; set; }

        public string DeviceId { get; set; }
        
        public static readonly SyncFilter DefaultFilter = new SyncFilter();
        
        public static SyncFilter FromJSON(string json)
        {
            return new SyncFilter();
        }

        public static SyncFilter FromDB(User user, string filterId)
        {
            return new SyncFilter();
        }
    }
}
