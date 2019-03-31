using System.ComponentModel.DataAnnotations.Schema;

namespace Matrix.SynapseInterop.Database.SynapseModels
{
    [Table("users_who_share_rooms")]
    public class UsersWhoShareRooms
    {
        [Column("user_id")]
        public string UserId { get; set; }

        [Column("other_user_id")]
        public string OtherUserId { get; set; }

        [Column("room_id")]
        public string RoomId { get; set; }

        [Column("share_private")]
        public bool SharePrivate { get; set; }
    }
}
