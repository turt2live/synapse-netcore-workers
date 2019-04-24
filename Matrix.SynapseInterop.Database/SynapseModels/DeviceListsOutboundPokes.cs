using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Matrix.SynapseInterop.Database.SynapseModels
{
    [Table("device_lists_outbound_pokes")]
    public class DeviceListsOutboundPokes
    {
        [Column("stream_id")]
        public int StreamId { get; set; }

        [Column("user_id")]
        public string UserId { get; set; }

        [Column("device_id")]
        public string DeviceId { get; set; }

        [Column("sent")]
        public bool Sent { get; set; }

        [Column("ts")]
        public long Ts { get; set; }

        [Column("destination")]
        public string Destination { get; set; }
    }
}
