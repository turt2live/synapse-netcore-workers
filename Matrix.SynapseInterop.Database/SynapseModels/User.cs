using System.ComponentModel.DataAnnotations.Schema;

namespace Matrix.SynapseInterop.Database.SynapseModels
{
    [Table("users")]
    public class User
    {
        [Column("name")]
        public string Name { get; set; }
        
        [Column("admin")]
        public int IsAdmin { get; set; }
        
        [Column("is_guest")]
        public int IsGuest { get; set; }

        [Column("appservice_id")]
        public string AppserviceId { get; set; }
    }
}
