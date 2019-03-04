using Matrix.SynapseInterop.Database.SynapseModels;
using Newtonsoft.Json.Linq;

namespace Matrix.SynapseInterop.Common.MatrixUtils
{
    public static class EventFormatter
    {
        public static JObject ToClientServerFormat(this EventJson ev)
        {
            var raw = JObject.Parse(ev.Json);

            var result = new JObject
            {
                {"sender", raw.GetValue("sender")},
                {"type", raw.GetValue("type")},
                {"content", raw.GetValue("content")},
                {"origin_server_ts", raw.GetValue("origin_server_ts")},
                {"event_id", ev.EventId},
                {"room_id", ev.RoomId}
            };

            if (raw.ContainsKey("state_key")) result.Add("state_key", raw.GetValue("state_key"));

            return result;
        }

        public static JObject ToAppserviceFormat(this EventJson ev)
        {
            return ev.ToClientServerFormat();
        }
    }
}
