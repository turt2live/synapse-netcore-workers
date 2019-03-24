using System.ComponentModel.DataAnnotations.Schema;

namespace Matrix.SynapseInterop.Database.SynapseModels
{
    [Table("event_push_summary")]
    public class EventPushSummary
    {
        [Column("room_id")]
        public string RoomId { get; set; }
        
        [Column("user_id")]
        public string UserId { get; set; }
        
        [Column("notif_count")]
        public int NotifCount { get; set; }
        
        [Column("stream_ordering")]
        public int StreamOrdering { get; set; }
    }
}
