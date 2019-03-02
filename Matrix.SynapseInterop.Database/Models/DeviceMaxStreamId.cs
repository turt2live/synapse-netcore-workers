using System.ComponentModel.DataAnnotations.Schema;

namespace Matrix.SynapseInterop.Database.Models
{
    [Table("device_max_stream_id")]
    public class DeviceMaxStreamId
    {
        [Column("stream_id")]
        public int StreamId { get; set; }
    }
}
