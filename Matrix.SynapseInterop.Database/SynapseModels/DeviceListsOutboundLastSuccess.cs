using System.ComponentModel.DataAnnotations.Schema;

namespace Matrix.SynapseInterop.Database.SynapseModels
{
    [Table("device_lists_outbound_last_success")]
    public class DeviceListsOutboundLastSuccess
    {
        [Column("stream_id")]
        public int StreamId { get; set; }

        [Column("user_id")]
        public string UserId { get; set; }

        [Column("destination")]
        public string Destination { get; set; }
    }
}
