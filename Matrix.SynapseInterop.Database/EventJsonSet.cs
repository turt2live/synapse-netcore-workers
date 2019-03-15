using System.Threading.Tasks;
using Matrix.SynapseInterop.Database.SynapseModels;
using Newtonsoft.Json.Linq;

namespace Matrix.SynapseInterop.Database
{
    public class EventJsonSet : Event
    {
        public int Version { get; private set; }
        private readonly Task _jsonTask;
        private JObject _json;
        
        public EventJsonSet(Event baseEvent, Task<EventJson> json)
        {
            EventId = baseEvent.EventId;
            RoomId = baseEvent.RoomId;
            StreamOrdering = baseEvent.StreamOrdering;
            Sender = baseEvent.Sender;

            // Lazy load in the content.
            _jsonTask = json.ContinueWith((e) =>
            {
                _json = JObject.Parse(e.Result.Json);
                Version = e.Result.FormatVersion;
            });

            _json = null;
            Version = -1;
        }

        public async Task<JObject> GetContent()
        {
            await _jsonTask;
            return _json;
        }
    }
}
