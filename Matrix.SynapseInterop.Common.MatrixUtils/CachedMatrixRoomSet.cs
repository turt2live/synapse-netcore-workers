using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Matrix.SynapseInterop.Common.Extensions;
using Matrix.SynapseInterop.Database;
using Matrix.SynapseInterop.Database.SynapseModels;
using Newtonsoft.Json.Linq;

namespace Matrix.SynapseInterop.Common.MatrixUtils
{
    public class CachedMatrixRoomSet
    {
        private readonly ConcurrentDictionary<string, CachedMatrixRoom> _dict;
        private readonly HashSet<string> _userMembershipComplete;

        public CachedMatrixRoomSet()
        {
            _dict = new ConcurrentDictionary<string, CachedMatrixRoom>();
            _userMembershipComplete = new HashSet<string>();
        }
        
        public CachedMatrixRoom GetRoom(string roomId, bool populateMemberCache = true)
        {
            if (_dict.TryGetValue(roomId, out var room))
            {
                WorkerMetrics.ReportCacheHit("CachedMatrixRoomSet.GetRoom");
                return room;
            }
            
            //TODO: Check to see if the room actually exists.

            WorkerMetrics.ReportCacheMiss("CachedMatrixRoomSet.GetRoom");

            room = new CachedMatrixRoom(roomId);

            if (populateMemberCache)
                room.PopulateMemberCache();

            _dict.TryAdd(roomId, room);
            return room;
        }

        public IEnumerable<CachedMatrixRoom> GetJoinedRoomsForUser(string userId, bool populateMemberCache = false)
        {
            if (_userMembershipComplete.Contains(userId))
            {
                WorkerMetrics.ReportCacheHit("CachedMatrixRoomSet.GetJoinedRoomsForUser");
                return _dict.Values.Where(r => r.Membership != null && r.Membership.Contains(userId));
            }

            WorkerMetrics.ReportCacheMiss("CachedMatrixRoomSet.GetJoinedRoomsForUser");

            using (var db = new SynapseDbContext())
            {
                var roomList = db.GetMembershipForUser(userId)
                                 .Where(m =>
                                            m.Membership == "join")
                                 .Select(room => GetRoom(room.RoomId, populateMemberCache));
                
                _userMembershipComplete.Add(userId);
                return roomList.ToList();
            }
        }

        public bool InvalidateRoom(string roomId)
        {
            var res = _dict.TryRemove(roomId, out var room);

            if (res)
                room.Membership.ForEach(u => _userMembershipComplete.Remove(u));

            return res;
        }

        public void FetchLatestEventsForRooms(IEnumerable<string> roomIds, int limit)
        {
            using (var db = new SynapseDbContext())
            {
                Dictionary<string, Event> eventSet = new Dictionary<string, Event>();

                foreach (var roomId in new HashSet<string>(roomIds))
                {
                    var events = db.Events.Where(e => e.RoomId == roomId).OrderByDescending(e => e.StreamOrdering)
                                   .Take(limit);

                    events.ForEach(ev => eventSet.Add(ev.EventId, ev));
                }
                
                // Get all the json in one go.
                foreach (var ev in 
                    db.EventsJson
                      .Where(ej => eventSet.ContainsKey(ej.EventId))
                      .Select(ej => new EventJsonSet(eventSet[ej.EventId], ej)).GroupBy(ev => ev.RoomId))
                {
                    GetRoom(ev.Key, false).PopulateEventCache(ev);
                }
            }
        }
    }

    public class CachedMatrixRoom
    {
        public readonly string RoomId;
        public List<string> Membership { get; private set; }
        public string[] Hosts { get; private set; }
        private readonly Dictionary<string, EventJsonSet> _eventCache;
        private readonly Dictionary<Tuple<string, string>, EventJsonSet> _state;
        private readonly StateGroupCache _stateGroupCache;
        
        public CachedMatrixRoom(string roomId)
        {
            RoomId = roomId;
            Membership = null;
            _eventCache = new Dictionary<string, EventJsonSet>();
            _state = new Dictionary<Tuple<string, string>, EventJsonSet>();
            _stateGroupCache = new StateGroupCache();
        }

        public EventJsonSet GetEvent(string eventId)
        {
            if (_eventCache.TryGetValue(eventId, out var res))
            {
                WorkerMetrics.ReportCacheHit("CachedMatrixRoom.GetEvent");
                return res;
            }

            WorkerMetrics.ReportCacheMiss("CachedMatrixRoom.GetEvent");

            using (var db = new SynapseDbContext())
            {
                var eventJsonSet = db
                                  .Events
                                  .Where(ev => ev.EventId == eventId)
                                  .Select(ev => new EventJsonSet(ev, null))
                                  .FirstOrDefault();

                if (eventJsonSet == null) return null;
                _eventCache.TryAdd(eventId, eventJsonSet);
                return eventJsonSet;
            }
        }

        public IEnumerable<EventJsonSet> GetLatestEvents(int limit)
        {
            return _eventCache.Values.OrderByDescending(ev => ev.StreamOrdering).Take(limit);
        }

        public IEnumerable<EventJsonSet> GetEventsAt(string eventId, int limit, bool forward = false)
        {
            using (var db = new SynapseDbContext())
            {
                var ev = GetEvent(eventId);

                var evs = db.Events.Where((e) => (forward ? e.StreamOrdering >= ev.StreamOrdering : e.StreamOrdering <= ev.StreamOrdering) && e.RoomId == ev.RoomId);

                evs = forward ? evs.OrderBy((e) => e.StreamOrdering) : evs.OrderByDescending((e) => e.StreamOrdering);
                
                return evs.Take(limit).Select((e) => GetEvent(e.EventId)).ToList();
            }
        }

        public async Task<bool> CanUserSeeEvent(string eventId, User user)
        {
            var state = GetStateAtEvent(eventId).ToArray();
            JObject content;
            string membershipAtEvent = "leave";
            string currentMembership = "leave";
            string visibility = "join";

            foreach (var ev in state)
            {
                if (ev.Type != "m.room.member" && ev.Type != "m.room.history_visibility")
                {
                    continue;
                }
                
                content = await ev.GetContent();
                var eventContent = content["content"] as JObject;

                if (ev.Type == "m.room.member" && (string)content["state_key"] == user.Name)
                {
                    membershipAtEvent = (string) eventContent?["membership"];
                    return membershipAtEvent == "join";
                }

                if (ev.Type == "m.room.history_visibility" && eventContent != null)
                {
                    visibility = (string) eventContent["visibility"];
                }
            }
            
            PopulateStateCache(false);

            foreach (var ev in GetCurrentState)
            {
                if (ev.Type != "m.room.member")
                {
                    continue;
                }

                content = await ev.GetContent();
                var eventContent = content["content"] as JObject;

                if (ev.Type == "m.room.member" && (string)content["state_key"] == user.Name)
                {
                    currentMembership = (string) eventContent?["membership"];
                }
            }

            if (visibility == "world_readable")
            {
                return true;
            } else if (currentMembership == "join" && visibility == "shared")
            {
                return true;
            } else if (membershipAtEvent == "invite" && visibility == "invite")
            {
                return true;
            }

            return false;
        }

        public void PopulateEventCache(IEnumerable<EventJsonSet> events)
        {
            foreach (var ev in events)
            {
                // Events are immutable, so if it conflicts then let it be.
                _eventCache.TryAdd(ev.EventId, ev);
            }
        }

        public void PopulateStateCache(bool dropCache = true)
        {
            // We are repopulating the state cache, so drop what we have.
            if (dropCache)
                _state.Clear();

            if (_state.Count > 0)
                return; // The cache has been filled and we don't want to discard it.
            
            using (var db = new SynapseDbContext())
            {
                foreach (var stateEvent in db.CrntRoomState.Where(ev => ev.RoomId == RoomId))
                {
                    var ev = GetEvent(stateEvent.EventId);

                    _state.Add(new Tuple<string, string>(stateEvent.Type, stateEvent.StateKey), ev);
                }
            }
        }

        public IEnumerable<EventJsonSet> GetStateAtEvent(string eventId)
        {
            return _stateGroupCache.GetStateForEvent(eventId).Select((e) => GetEvent(e.EventId));
        }

        public void PopulateMemberCache()
        {
            using (var db = new SynapseDbContext())
            {
                var members = db.RoomMemberships.Where(r => r.RoomId == RoomId && r.Membership == "join").Select(r => r.UserId);
                Hosts = members.Select(m => m.Split(":", StringSplitOptions.None)[1]).ToArray();
                Membership = members.ToList();
            }
        }

        public IEnumerable<EventJsonSet> GetCurrentState => _state.Values.ToList();
    }
}
