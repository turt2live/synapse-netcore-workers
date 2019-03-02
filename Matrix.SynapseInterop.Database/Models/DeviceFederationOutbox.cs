using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Matrix.SynapseInterop.Database.Models
{
    [Table("device_federation_outbox")]
    public class DeviceFederationOutbox
    {
        [Column("destination")]
        public string Destination { get; set; }

        [Column("stream_id")]
        [Key]
        public int StreamId { get; set; }

        [Column("queued_ts")]
        public long QueuedTs { get; set; }

        [Column("messages_json")]
        public string MessagesJson { get; set; }
    }
}
