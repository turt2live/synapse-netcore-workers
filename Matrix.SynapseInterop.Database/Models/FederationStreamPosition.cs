using System.ComponentModel.DataAnnotations.Schema;

namespace Matrix.SynapseInterop.Database.Models
{
    [Table("federation_stream_position")]
    public class FederationStreamPosition
    {
        [Column("type")]
        public string Type { get; set; }

        [Column("stream_id")]
        public int StreamId { get; set; }
    }
}