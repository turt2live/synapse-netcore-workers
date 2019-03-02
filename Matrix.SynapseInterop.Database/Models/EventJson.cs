using System.ComponentModel.DataAnnotations.Schema;

namespace Matrix.SynapseInterop.Database.Models
{
    [Table("event_json")]
    public class EventJson
    {
        [Column("event_id")]
        public string EventId { get; set; }

        [Column("room_id")]
        public string RoomId { get; set; }

        [Column("internal_metadata")]
        public string InternalMetadata { get; set; }

        [Column("json")]
        public string Json { get; set; }

        [Column("format_version")]
        public int FormatVersion { get; set; }
    }
}
