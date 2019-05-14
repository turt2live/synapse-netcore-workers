using System.ComponentModel.DataAnnotations.Schema;

namespace Matrix.SynapseInterop.Database.SynapseModels
{
    [Table("event_to_state_groups")]
    public class EventToStateGroup
    {
        [Column("event_id")]
        public string EventId { get; set; }
        
        [Column("state_group")]
        public int StateGroup { get; set; }
    }
    
    [Table("state_groups")]
    public class StateGroup
    {
        [Column("id")]
        public int Id { get; set; }
        
        [Column("event_id")]
        public string EventId { get; set; }
        
        [Column("room_id")]
        public string RoomId { get; set; }
    }
        
    [Table("state_groups_state")]
    public class StateGroupsState
    {
        [Column("state_group")]
        public int StateGroup { get; set; }
        
        [Column("event_id")]
        public string EventId { get; set; }
        
        [Column("room_id")]
        public string RoomId { get; set; }
        
        [Column("state_key")]
        public string StateKey { get; set; }
        
        [Column("type")]
        public string Type { get; set; }
    }
    
    [Table("state_group_edges")]
    public class StateGroupEdge
    {
        [Column("state_group")]
        public int StateGroup { get; set; }
        
        [Column("prev_state_group")]
        public int PreviousStateGroup { get; set; }
    }
}
