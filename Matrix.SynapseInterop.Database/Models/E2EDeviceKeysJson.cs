using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Matrix.SynapseInterop.Database.Models
{
    [Table("e2e_device_keys_json")]
    public class E2EDeviceKeysJson
    {
        [Column("user_id")]
        public string UserId { get; set; }
        
        [Column("device_id")]
        public string DeviceId { get; set; }
      
        [Column("ts_added_ms")]
        public long TsAddedMs { get; set; }
        
        [Column("key_json")]
        public string KeyJson { get; set; }

    }
}