using System.ComponentModel.DataAnnotations.Schema;

namespace Matrix.SynapseInterop.Database.SynapseModels
{
    [Table("device_inbox")]
    public class DeviceInboxItem
    {
        [Column("stream_id")]
        public int StreamId { get; set; }

        [Column("user_id")]
        public string UserId { get; set; }

        [Column("device_id")]
        public string DeviceId { get; set; }
        
        [Column("message_json")]
        public string MessageJson { get; set; }
    }
}
