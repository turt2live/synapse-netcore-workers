using System.Collections.Generic;
using System.Linq;
using Matrix.SynapseInterop.Common;
using Matrix.SynapseInterop.Common.Extensions;
using Matrix.SynapseInterop.Database.SynapseModels;

namespace Matrix.SynapseInterop.Database
{
    public class StateGroupCache
    {
        public List<StateGroupsState> GetStateForEvent(EventJsonSet ev) => GetStateForEvent(ev.EventId);
        public List<StateGroupsState> GetStateForEvent(Event ev) => GetStateForEvent(ev.EventId);

        private readonly Dictionary<int, List<StateGroupsState>> _stateGroupStateCache = new Dictionary<int, List<StateGroupsState>>();
        private readonly Dictionary<string, int> _stateGroupForEvent = new Dictionary<string, int>();
      
        public List<StateGroupsState> GetStateForEvent(string eventId)
        {   
            if (_stateGroupForEvent.TryGetValue(eventId, out var stateGroup))
            {
                if (_stateGroupStateCache.TryGetValue(stateGroup, out var state))
                {
                    WorkerMetrics.ReportCacheHit("StateGroupCache.GetStateForEvent");
                    return state;
                }
            }
            
            WorkerMetrics.ReportCacheMiss("StateGroupCache.GetStateForEvent");
            
            using (var db = new SynapseDbContext())
            {
                stateGroup = db.EventToStateGroups.First(eventStateGroup => eventStateGroup.EventId == eventId).StateGroup;

                if (_stateGroupStateCache.TryGetValue(stateGroup, out var state))
                {
                    WorkerMetrics.ReportCacheHit("StateGroupCache.GetStateForGroup");
                    return _stateGroupStateCache[stateGroup];
                }

                WorkerMetrics.ReportCacheMiss("StateGroupCache.GetStateForGroup");
                
                state = GetStateForGroup(stateGroup);
                _stateGroupStateCache.Add(stateGroup, state);
                return state;
            }
        }

        private List<StateGroupsState> GetStateForGroup(int stateGroup)
        {
            using (var db = new SynapseDbContext())
            {
                // Get edges for this state group chain.
                HashSet<int> stateGroups = new HashSet<int>(new[] {stateGroup});

                db.StateGroupEdges.OrderByDescending(edge => edge.StateGroup).ForEach(edge =>
                {
                    if (stateGroups.Contains(edge.StateGroup))
                    {
                        stateGroups.Add(edge.PreviousStateGroup);
                    }
                });

                // We should now have some splendid edges.
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
