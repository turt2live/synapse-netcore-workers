using System.ComponentModel.DataAnnotations.Schema;

namespace Matrix.SynapseInterop.Database.SynapseModels
{
    [Table("access_tokens")]
    public class AccessToken
    {
        [Column("user_id")]
        public string UserId { get; set; }
        
        [Column("device_id")]
        public string DeviceId { get; set; }
        
        [Column("token")]
        public string Token { get; set; }
    }
}
