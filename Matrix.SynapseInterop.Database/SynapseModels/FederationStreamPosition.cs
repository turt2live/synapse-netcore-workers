using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Matrix.SynapseInterop.Database.SynapseModels
{
    [Table("federation_stream_position")]
    public class FederationStreamPosition
    {
        [Key]
        [Column("type")]
        public string Type { get; set; }

        [Column("stream_id")]
        public int StreamId { get; set; }
    }
}
