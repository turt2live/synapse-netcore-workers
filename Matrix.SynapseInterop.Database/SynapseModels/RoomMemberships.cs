using System.ComponentModel.DataAnnotations.Schema;

namespace Matrix.SynapseInterop.Database.SynapseModels
{
    [Table("room_memberships")]
    public class RoomMembership
    {
        [Column("event_id")]
        public string EventId { get; set; }

        [Column("user_id")]
        public string UserId { get; set; }

        [Column("sender")]
        public string Sender { get; set; }

        [Column("room_id")]
        public string RoomId { get; set; }

        [Column("membership")]
        public string Membership { get; set; }

        [Column("forgotten")]
        public int Forgotten { get; set; }
    }
}
