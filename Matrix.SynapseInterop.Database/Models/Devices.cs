using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Matrix.SynapseInterop.Database.Models
{
    [Table("devices")]
    public class Devices
    {
        [Column("user_id")]
        public string UserId { get; set; }
        
        [Column("device_id")]
        public string DeviceId { get; set; }
      
        [Column("display_name")]
        public string DisplayName { get; set; }
    }
}