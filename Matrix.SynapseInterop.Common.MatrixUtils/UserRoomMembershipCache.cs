using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Matrix.SynapseInterop.Database;
using Matrix.SynapseInterop.Database.SynapseModels;

namespace Matrix.SynapseInterop.Common.MatrixUtils
{
    public class UserRoomMembershipCache
    {
        private readonly Dictionary<string, List<string>> _joinedRooms; // userId => roomId
        
        public UserRoomMembershipCache()
        {
            _joinedRooms = new Dictionary<string, List<string>>();
        }

        public List<string> GetJoinedRoomsForUser(string userId)
        {
            if (_joinedRooms.TryGetValue(userId, out var memberships))
            {
                WorkerMetrics.ReportCacheHit("user_room_membership_cache");
                return memberships;
            }

            WorkerMetrics.ReportCacheMiss("user_room_membership_cache");

            using (var db = new SynapseDbContext())
            {
                var membershipList = db.RoomMemberships
                                       .Where(m =>
                                                  m.Membership == "join" &&
                                                  m.UserId == userId).Select(m => m.RoomId).ToList();

                _joinedRooms.Add(userId, membershipList);
                return membershipList;
            }
        }

        public bool InvalidateCache(string userId) => _joinedRooms.Remove(userId);
    }
}
