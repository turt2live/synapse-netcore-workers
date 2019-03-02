using System.ComponentModel.DataAnnotations.Schema;

namespace Matrix.SynapseInterop.Database.Models
{
    [Table("events")]
    public class Event
    {
        [Column("stream_ordering")]
        public int StreamOrdering { get; set; }

        [Column("event_id")]
        public string EventId { get; set; }

        [Column("room_id")]
        public string RoomId { get; set; }

        [Column("sender")]
        public string Sender { get; set; }
    }
}
