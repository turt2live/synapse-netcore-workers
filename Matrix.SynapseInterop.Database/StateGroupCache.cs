using System.Collections.Generic;
using System.Linq;
using Matrix.SynapseInterop.Common.Extensions;
using Matrix.SynapseInterop.Database.SynapseModels;

namespace Matrix.SynapseInterop.Database
{
    public class StateGroupCache
    {
        public List<StateGroupsState> GetStateForEvent(EventJsonSet ev) => GetStateForEvent(ev.EventId);
        public List<StateGroupsState> GetStateForEvent(Event ev) => GetStateForEvent(ev.EventId);
        
        public List<StateGroupsState> GetStateForEvent(string eventId)
        {
            using (var db = new SynapseDbContext())
            {
                var stateGroup = db.EventToStateGroups.First(eventStateGroup => eventStateGroup.EventId == eventId).StateGroup;
                // Get edges for this state group chain.
                HashSet<int> stateGroups = new HashSet<int>(new[] {stateGroup});

                db.StateGroupEdges.OrderByDescending(edge => edge.StateGroup).ForEach(edge =>
                {
                    if (stateGroups.Contains(edge.StateGroup))
                    {
                        stateGroups.Add(edge.PreviousStateGroup);
                    }
                });
                
                // We should now have some spendid edges.
                List<StateGroupsState> roomState = new List<StateGroupsState>();

                foreach (var sG in stateGroups.OrderBy(i => i))
                {
                    db.StateGroupsStates.Where((groupState) => groupState.StateGroup == sG).ForEach((groupState) =>
                    {
                        roomState.RemoveAll(s => s.Type == groupState.Type && s.StateKey == groupState.StateKey);
                        roomState.Add(groupState);
                    });
                }

                return roomState;
            }
        }
    }
}
