﻿using System.Collections.Generic;
using System.Linq;
using Matrix.SynapseInterop.Common;
using Matrix.SynapseInterop.Database.SynapseModels;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Matrix.SynapseInterop.Database
{
    public class SynapseDbContext : DbContext
    {
        private readonly string _connString;
        public static string DefaultConnectionString { get; set; }

        public DbQuery<EventJson> EventsJson { get; set; }
        public DbQuery<Event> Events { get; set; }
        public DbQuery<RoomMembership> RoomMemberships { get; set; }
        public DbSet<FederationStreamPosition> FederationStreamPosition { get; set; }
        public DbSet<DeviceFederationOutbox> DeviceFederationOutboxes { get; set; }
        public DbSet<DeviceListsOutboundPokes> DeviceListsOutboundPokes { get; set; }
        private DbQuery<E2EDeviceKeysJson> E2EDeviceKeysJson { get; set; }
        private DbQuery<Devices> Devices { get; set; }
        public DbQuery<RoomAlias> RoomAliases { get; set; }

        public SynapseDbContext() : this(DefaultConnectionString) { }

        public SynapseDbContext(string connectionString)
        {
            _connString = connectionString;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var ctx = Log.ForContext<SynapseDbContext>();
            SerilogLoggerFactory s = new SerilogLoggerFactory();
            optionsBuilder.UseNpgsql(_connString).UseLoggerFactory(s);
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
                    var contentSet = new DeviceContentSet
                    {
                        device_id = devicePoke.DeviceId,
                        stream_id = devicePoke.StreamId,
                        user_id = devicePoke.UserId,
                        deleted = false
                    };

                    var json = E2EDeviceKeysJson
                       .SingleOrDefault(devKeys => devKeys.DeviceId == devicePoke.DeviceId &&
                                                   devKeys.UserId == devicePoke.UserId);

                    if (json != null) contentSet.keys = JObject.Parse(json.KeyJson);

                    contentSet.device_display_name = Devices.SingleOrDefault(dev =>
                                                                                 dev.UserId == devicePoke.UserId &&
                                                                                 dev.DeviceId == devicePoke.DeviceId)
                                                           ?.DisplayName;

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
