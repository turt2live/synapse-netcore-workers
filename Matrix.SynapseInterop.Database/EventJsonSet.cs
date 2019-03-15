using System.Linq;
using System.Threading.Tasks;
using Matrix.SynapseInterop.Common;
using Matrix.SynapseInterop.Database.SynapseModels;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace Matrix.SynapseInterop.Database
{
    public class EventJsonSet : Event
    {
        public int Version { get; private set; }
        private JObject _json;
        
        public EventJsonSet(Event baseEvent)
        {
            EventId = baseEvent.EventId;
            RoomId = baseEvent.RoomId;
            StreamOrdering = baseEvent.StreamOrdering;
            Sender = baseEvent.Sender;
            _json = null;
            Version = -1;
        }

        public async Task<JObject> GetContent()
        {
            if (_json != null)
            {
                return _json;
            }

            // We lazy-load the content to save a few calls if we exit early.
            using (var ctx = new SynapseDbContext())
            {
                using (WorkerMetrics.DbCallTimer("EventsJson.GetContent"))
                { 
                    var js = await ctx.EventsJson.AsNoTracking().Where(e => e.EventId == EventId).FirstAsync();
                    Version = js.FormatVersion;
                    _json = JObject.Parse(js.Json);
                }
            }
        
            return _json;
        }
    }
}
