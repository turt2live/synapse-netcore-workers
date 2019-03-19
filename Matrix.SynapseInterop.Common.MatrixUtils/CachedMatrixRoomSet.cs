using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Matrix.SynapseInterop.Database;
using Matrix.SynapseInterop.Database.SynapseModels;

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
            return room;
        }

        public IEnumerable<CachedMatrixRoom> GetJoinedRoomsForUser(string userId)
        {
            if (_userMembershipComplete.Contains(userId))
            {
                WorkerMetrics.ReportCacheHit("CachedMatrixRoomSet.GetJoinedRoomsForUser");
                return _dict.Values.Where(r => r.Membership.Contains(userId));
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
    }

    public class CachedMatrixRoom
    {
        public readonly string RoomId;
        public List<string> Membership { get; private set; }
        public string[] Hosts { get; private set; }
        
        public CachedMatrixRoom(string roomId)
        {
            RoomId = roomId;
            Membership = null;
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
