using System.ComponentModel.DataAnnotations.Schema;

namespace Matrix.SynapseInterop.Database.SynapseModels
{
    [Table("e2e_one_time_keys_json")]
    public class E2EOneTimeKey
    {
        [Column("user_id")]
        public string UserId { get; set; }

        [Column("device_id")]
        public string DeviceId { get; set; }

        [Column("algorithm")]
        public string Algorithm { get; set; }

        [Column("key_id")]
        public string KeyId { get; set; }

        [Column("ts_added_ms")]
        public int TsAddedMs { get; set; }

        [Column("key_json")]
        public string Json { get; set; }
    }
}
