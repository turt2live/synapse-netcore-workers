using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Matrix.SynapseInterop.Database.SynapseModels
{
    [Table("device_lists_outbound_pokes")]
    public class DeviceListsOutboundPokes
    {
        [Column("stream_id")]
        [Key]
        public int StreamId { get; set; }

        [Column("user_id")]
        [Key]
        public string UserId { get; set; }

        [Column("device_id")]
        [Key]
        public string DeviceId { get; set; }

        [Column("sent")]
        public bool Sent { get; set; }

        [Column("ts")]
        public long Ts { get; set; }

        [Column("destination")]
        [Key]
        public string Destination { get; set; }
    }
}
