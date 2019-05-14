using System.ComponentModel.DataAnnotations.Schema;

namespace Matrix.SynapseInterop.Database.SynapseModels
{
    [Table("room_account_data")]
    public class RoomAccountData
    {
        [Column("room_id")]
        public string RoomId { get; set; }
        
        [Column("user_id")]
        public string UserId { get; set; }

        [Column("account_data_type")]
        public string Type { get; set; }

        [Column("stream_id")]
        public int StreamId { get; set; }

        [Column("content")]
        public string Content { get; set; }
    }
}
