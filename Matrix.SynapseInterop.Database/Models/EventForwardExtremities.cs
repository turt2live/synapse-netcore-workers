using System.ComponentModel.DataAnnotations.Schema;

namespace Matrix.SynapseInterop.Database.Models
{
    [Table("event_forward_extremities")]
    public class EventForwardExtremities
    {
        [Column("room_id")]
        public string RoomId { get; set; }

        [Column("event_id")]
        public string EventId { get; set; }
    }
}
