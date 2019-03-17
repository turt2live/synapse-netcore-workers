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

        public CachedMatrixRoomSet()
        {
            _dict = new ConcurrentDictionary<string, CachedMatrixRoom>();
        }
        
        public CachedMatrixRoom GetRoom(string roomId)
        {
            if (_dict.TryGetValue(roomId, out var room))
            {
                WorkerMetrics.ReportCacheHit("cached_matrix_room_set");
                return room;
            }

            WorkerMetrics.ReportCacheMiss("cached_matrix_room_set");

            room = new CachedMatrixRoom(roomId);
            room.PopulateMemberCache();
            _dict.TryAdd(roomId, room);
            return room;
        }
    }

    public class CachedMatrixRoom
    {
        private readonly string _roomId;
        public string[] Membership { get; private set; }
        public string[] Hosts { get; private set; }
        
        public CachedMatrixRoom(string roomId)
        {
            _roomId = roomId;
            Membership = null;
        }

        public void PopulateMemberCache()
        {
            using (var db = new SynapseDbContext())
            {
                var members = db.RoomMemberships.Where(r => r.RoomId == _roomId && r.Membership == "join").Select(r => r.UserId);
                Hosts = members.Select(m => m.Split(":", StringSplitOptions.None)[1]).ToArray();
                Membership = members.ToArray();
            }
        }
    }
}
