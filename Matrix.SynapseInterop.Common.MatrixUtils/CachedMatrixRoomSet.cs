using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Matrix.SynapseInterop.Common.Extensions;
using Matrix.SynapseInterop.Database;
using Matrix.SynapseInterop.Database.SynapseModels;

namespace Matrix.SynapseInterop.Common.MatrixUtils
{
    public class CachedMatrixRoomSet
    {
        private readonly ConcurrentDictionary<string, CachedMatrixRoom> _dict;
        private readonly HashSet<string> _userMembershipComplete;
        private readonly int maxSize;

        public CachedMatrixRoomSet(int maxSize = -1)
        {
            _dict = new ConcurrentDictionary<string, CachedMatrixRoom>();
            _userMembershipComplete = new HashSet<string>();
            this.maxSize = maxSize;
        }
        
        public CachedMatrixRoom GetRoom(string roomId)
        {
            if (_dict.TryGetValue(roomId, out var room))
            {
                WorkerMetrics.ReportCacheHit("CachedMatrixRoomSet.GetRoom");
                return room;
            }

            WorkerMetrics.ReportCacheMiss("CachedMatrixRoomSet.GetRoom");

            room = new CachedMatrixRoom(roomId);
            room.PopulateMemberCache();
            _dict.TryAdd(roomId, room);
            CheckMaxSize();
            return room;
        }

        public IEnumerable<CachedMatrixRoom> GetJoinedRoomsForUser(string userId)
        {
            if (_userMembershipComplete.Contains(userId))
            {
                WorkerMetrics.ReportCacheHit("CachedMatrixRoomSet.GetJoinedRoomsForUser");

                return _dict.Values.Where(r =>
                {
                    var c = r.Membership.Contains(userId);

                    if (c)
                    {
                        r.LastAccessed = DateTime.Now;
                    }

                    return c;
                });
            }

            WorkerMetrics.ReportCacheMiss("CachedMatrixRoomSet.GetJoinedRoomsForUser");

            using (var db = new SynapseDbContext())
            {
                var roomList = db.RoomMemberships
                                 .Where(m =>
                                            m.Membership == "join" &&
                                            m.UserId == userId)
                                 .Select(room => GetRoom(room.RoomId));
                
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

        private void CheckMaxSize()
        {
            if (maxSize == -1)
            {
                return;
            }

            var dropCount = _dict.Count - maxSize;

            if (dropCount == 0)
            {
                return;
            }

            _dict.OrderBy((pair => pair.Value.LastAccessed))
                 .Select((pair => pair.Key))
                 .ForEach((r) => InvalidateRoom(r));
        }
    }

    public class CachedMatrixRoom
    {
        public DateTime LastAccessed;
        
        public readonly string RoomId;
        public List<string> Membership { get; private set; }
        public string[] Hosts { get; private set; }
        
        public CachedMatrixRoom(string roomId)
        {
            RoomId = roomId;
            Membership = null;
            LastAccessed = DateTime.Now; 
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
    }
}
