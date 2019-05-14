using System.Collections.Generic;
using System.Linq;
using Matrix.SynapseInterop.Common;
using Matrix.SynapseInterop.Database.SynapseModels;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using Serilog;
using Serilog.AspNetCore;

namespace Matrix.SynapseInterop.Database
{
    public class SynapseDbContext : DbContext
    {
        private readonly string _connString;
        public static string DefaultConnectionString { get; set; }
        private static readonly SerilogLoggerFactory LoggerFactory = new SerilogLoggerFactory(Log.ForContext<SynapseDbContext>());

        public DbQuery<RoomAccountData> RoomAccountData { get; set; }
        public DbQuery<AccountData> AccountData { get; set; }
        public DbQuery<EventPushSummary> EventPushSummary { get; set; }
        public DbQuery<EventJson> EventsJson { get; set; }
        public DbQuery<Event> Events { get; set; }
        public DbQuery<RoomMembership> RoomMemberships { get; set; }
        public DbSet<FederationStreamPosition> FederationStreamPosition { get; set; }
        public DbSet<DeviceFederationOutbox> DeviceFederationOutboxes { get; set; }
        public DbSet<DeviceListsOutboundPokes> DeviceListsOutboundPokes { get; set; }
        private DbQuery<E2EDeviceKeysJson> E2EDeviceKeysJson { get; set; }
        private DbQuery<Devices> Devices { get; set; }
        public DbQuery<RoomAlias> RoomAliases { get; set; }
        public DbQuery<AccessToken> AccessTokens { get; set; }
        public DbQuery<User> Users { get; set; }
        public DbQuery<CurrentStateEvents> CrntRoomState { get; set; }
        
        public DbQuery<EventToStateGroup> EventToStateGroups { get; set; }
        public DbQuery<StateGroup> StateGroups { get; set; }
        public DbQuery<StateGroupsState> StateGroupsStates { get; set; }
        public DbQuery<StateGroupEdge> StateGroupEdges { get; set; }
        public DbQuery<DeviceMaxStreamId> DeviceMaxStreamId { get; set; }
        public DbQuery<DeviceInboxItem> DeviceInbox { get; set; }
        public DbQuery<E2EOneTimeKey> E2EOneTimeKeys { get; set; }
        public DbQuery<DeviceListsStream> DeviceListsStream { get; set; }
        public DbQuery<UsersWhoShareRooms> UsersWhoShareRooms { get; set; }

        public SynapseDbContext() : this(DefaultConnectionString) { }

        public SynapseDbContext(string connectionString)
        {
            _connString = connectionString;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DeviceListsOutboundPokes>()
                .HasKey(c => new { c.StreamId, c.UserId, c.DeviceId, c.Destination });
            modelBuilder.Entity<DeviceFederationOutbox>()
                .HasKey(c => new { c.StreamId, c.Destination });
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql(_connString).UseLoggerFactory(LoggerFactory);
        }
        
        public List<DeviceContentSet> GetNewDevicesForDestination(string destination, int limit)
        {
            using (WorkerMetrics.DbCallTimer("GetNewDevicesForDestination"))
            {
                var set = new List<DeviceContentSet>();

                foreach (var devicePoke in DeviceListsOutboundPokes
                                          .Where(poke => poke.Destination == destination && poke.Sent == false)
                                          .Take(limit)
                                          .OrderBy(poke => poke.StreamId).ToList())
                {
                    var device = Devices.SingleOrDefault(dev =>
                                                             dev.UserId == devicePoke.UserId &&
                                                             dev.DeviceId == devicePoke.DeviceId);

                    var previousIds = DeviceListsOutboundPokes.Where(dev => 
                                                                         dev.UserId == devicePoke.UserId &&
                                                                         dev.DeviceId == devicePoke.DeviceId &&
                                                                         dev.StreamId < devicePoke.StreamId).Select(poke => poke.StreamId).ToArray();
                    
                    var contentSet = new DeviceContentSet
                    {
                        device_id = devicePoke.DeviceId,
                        stream_id = devicePoke.StreamId,
                        user_id = devicePoke.UserId,
                        deleted = device == null,
                        prev_id = previousIds,
                    };

                    var json = E2EDeviceKeysJson
                       .SingleOrDefault(devKeys => devKeys.DeviceId == devicePoke.DeviceId &&
                                                   devKeys.UserId == devicePoke.UserId);

                    if (json != null)
                        contentSet.keys = JObject.Parse(json.KeyJson);

                    if (device != null)
                        contentSet.device_display_name = device.DisplayName;

                    set.Add(contentSet);
                }

                return set;
            }
        }

        public IEnumerable<EventJsonSet> GetAllNewEventsStream(int fromId, int currentId, int limit = -1)
        {
            using (WorkerMetrics.DbCallTimer("GetAllNewEventsStream"))
            {
                // No tracking optimisation, we are unlikely to need the same event twice for the federation sender.
                var s = Events
                       .AsNoTracking()
                       .OrderBy(e => e.StreamOrdering)
                       .Where(e => e.StreamOrdering > fromId && e.StreamOrdering <= currentId);

                if (limit != -1)
                {
                    s = s.Take(limit);
                }

                return s.Select(ev => new EventJsonSet(ev, null));
            }
        }

        public bool GetUserForToken(string accessToken, out User user, out AccessToken token)
        {
            user = null;
            token = null;

            var aToken = AccessTokens.FirstOrDefault(at => at.Token == accessToken);

            if (aToken == null)
            {
                return false;
            }
            
            token = aToken;
            user = Users.FirstOrDefault(u => u.Name == aToken.UserId);
            return user != null;
        }

        public IQueryable<RoomMembership> GetMembershipForUser(string userId)
        {
            // RoomMemberships can lie about current membership, so check against the room state.
            return RoomMemberships.Where((u) => u.UserId == userId && CrntRoomState.Any(e => e.EventId == u.EventId));
        }
    }
}
