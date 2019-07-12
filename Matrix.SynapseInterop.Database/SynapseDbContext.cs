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

        public DbQuery<EventJson> EventsJson { get; set; }
        public DbQuery<Event> Events { get; set; }
        public DbQuery<RoomMembership> RoomMemberships { get; set; }
        public DbSet<FederationStreamPosition> FederationStreamPosition { get; set; }
        public DbSet<DeviceFederationOutbox> DeviceFederationOutboxes { get; set; }
        public DbSet<DeviceListsOutboundPokes> DeviceListsOutboundPokes { get; set; }
        public DbSet<DeviceListsOutboundLastSuccess> DeviceListsOutboundLastSuccess { get; set; }
        private DbQuery<E2EDeviceKeysJson> E2EDeviceKeysJson { get; set; }
        private DbQuery<Devices> Devices { get; set; }
        public DbQuery<RoomAlias> RoomAliases { get; set; }

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
                    
                    // Get the last event sent to this destination successfully.
                    var previousId =
                        DeviceListsOutboundLastSuccess
                           .OrderByDescending(s => s.StreamId)
                           .FirstOrDefault(success => devicePoke.UserId == success.UserId && 
                                                      devicePoke.Destination == success.Destination && 
                                                      devicePoke.StreamId > success.StreamId);

                    var previousIds = previousId == default ? new int[0] : new int[] {previousId.StreamId};
                    
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

        public IEnumerable<EventJsonSet> GetAllNewEventsStream(int fromId, int currentId, int limit)
        {
            using (WorkerMetrics.DbCallTimer("GetAllNewEventsStream"))
            {
                // No tracking optimisation, we are unlikely to need the same event twice for the federation sender.
                return Events
                      .AsNoTracking()
                      .OrderBy(e => e.StreamOrdering)
                      .Where(e => e.StreamOrdering > fromId && e.StreamOrdering <= currentId)
                      .Take(limit).Select(ev => new EventJsonSet(ev));
            }
        }
    }
}
