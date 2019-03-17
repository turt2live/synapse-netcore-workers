using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Matrix.SynapseInterop.Database;
using Matrix.SynapseInterop.Database.SynapseModels;

namespace Matrix.SynapseInterop.Common.MatrixUtils
{
    public class UserRoomMembershipCache
    {
        private readonly Dictionary<string, List<RoomMembership>> _joinedRooms; // userId => RoomMembership
        
        public UserRoomMembershipCache()
        {
            _joinedRooms = new Dictionary<string, List<RoomMembership>>();
        }

        public List<string> GetJoinedRoomsForUser(string userId)
        {
            if (_joinedRooms.TryGetValue(userId, out var memberships))
            {
                WorkerMetrics.ReportCacheHit("user_room_membership_cache");
                return memberships.Where(m => m.Membership == "join").Select(m => m.RoomId).ToList();
            }

            WorkerMetrics.ReportCacheMiss("user_room_membership_cache");

            using (var db = new SynapseDbContext())
            {
                var membershipList = db.RoomMemberships
                                       .Where(m =>
                                                  m.Membership == "join" &&
                                                  m.UserId == userId);

                _joinedRooms.Add(userId, membershipList.ToList());
                return membershipList.Where(m => m.Membership == "join").Select(m => m.RoomId).ToList();
            }
        }

        public bool InvalidateCache(string userId) => _joinedRooms.Remove(userId);
    }
}
