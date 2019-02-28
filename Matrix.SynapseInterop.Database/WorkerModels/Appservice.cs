using System.ComponentModel.DataAnnotations.Schema;

namespace Matrix.SynapseInterop.Database.WorkerModels
{
    [Table("appservices")]
    public class Appservice
    {
        // TODO: Namespace support

        [Column("id")]
        public string Id { get; set; }

        [Column("enabled")]
        public bool Enabled { get; set; }

        [Column("as_token")]
        public string AppserviceToken { get; set; }

        [Column("hs_token")]
        public string HomeserverToken { get; set; }

        [Column("url")]
        public string Url { get; set; }

        [Column("sender_localpart")]
        public string SenderLocalpart { get; set; }

        [Column("metadata")]
        public string Metadata { get; set; }
    }
}
