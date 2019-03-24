using System.ComponentModel.DataAnnotations.Schema;

namespace Matrix.SynapseInterop.Database.SynapseModels
{
    [Table("current_state_events")]
    public class CurrentStateEvents
    {
        [Column("event_id")]
        public string EventId { get; set; }

        [Column("room_id")]
        public string RoomId { get; set; }

        [Column("state_key")]
        public string StateKey { get; set; }

        [Column("type")]
        public string Type { get; set; }
    }
}
