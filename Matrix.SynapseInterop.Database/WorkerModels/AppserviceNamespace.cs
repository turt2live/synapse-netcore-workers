using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Matrix.SynapseInterop.Database.WorkerModels
{
    [Table("appservice_namespaces")]
    public class AppserviceNamespace
    {
        public static readonly string NS_USERS = "users";
        public static readonly string NS_ROOMS = "rooms";
        public static readonly string NS_ALIASES = "aliases";

        [Key]
        [Column("id")]
        public string Id { get; set; }

        [ForeignKey("id")]
        [Column("appservice_id")]
        public string AppserviceId { get; set; }

        [Column("kind")]
        public string Kind { get; set; }

        [Column("exclusive")]
        public bool Exclusive { get; set; }

        [Column("regex")]
        public string Regex { get; set; }
    }
}
