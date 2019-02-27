using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Matrix.SynapseInterop.Database.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace Matrix.SynapseInterop.Database
{
    public class SynapseDbContext : DbContext
    {
        public DbQuery<EventJson> EventsJson { get; set; }
        public DbQuery<Event> Events { get; set; }
        public DbQuery<EventForwardExtremities> EventForwardExtremities { get; set; }
        public DbQuery<RoomMemberships> RoomMemberships { get; set; }
        public DbSet<FederationStreamPosition> FederationStreamPosition { get; set; }
        public DbSet<DeviceFederationOutbox> DeviceFederationOutboxes { get; set; }
        public DbQuery<DeviceMaxStreamId> DeviceMaxStreamId { get; set; }
        public DbQuery<E2EDeviceKeysJson> E2EDeviceKeysJson { get; set; }
        public DbQuery<Devices> Devices { get; set; }
        public DbSet<DeviceListsOutboundPokes> DeviceListsOutboundPokes { get; set; }

        private string _connString;

        public SynapseDbContext(string connectionString)
        {
            this._connString = connectionString;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql(_connString);
        }

        public List<DeviceContentSet> GetNewDevicesForDestination(string destination, int limit)
        {
            List<DeviceContentSet> set = new List<DeviceContentSet>();
            foreach (var devicePoke in DeviceListsOutboundPokes
                .Where(poke => poke.Destination == destination && poke.Sent == false)
                .Take(limit)
                .OrderBy(poke => poke.StreamId).ToList())
            {
                DeviceContentSet contentSet = new DeviceContentSet
                {
                    device_id = devicePoke.DeviceId,
                    stream_id = devicePoke.StreamId,
                    user_id = devicePoke.UserId,
                    deleted = false,
                };
                var json = E2EDeviceKeysJson
                    .SingleOrDefault(devKeys => devKeys.DeviceId == devicePoke.DeviceId && devKeys.UserId == devicePoke.UserId);
                contentSet.keys = JObject.Parse(json.KeyJson);
                contentSet.device_display_name = Devices.SingleOrDefault(dev =>
                    dev.UserId == devicePoke.UserId && dev.DeviceId == devicePoke.DeviceId)
                    ?.DisplayName;
                set.Add(contentSet);
            }
            return set;
        }

        public List<EventJsonSet> GetAllNewEventsStream(int fromId, int currentId, int limit)
        {
            List<EventJsonSet> set = new List<EventJsonSet>();
            foreach (var ev in Events
                .Where(e => e.StreamOrdering > fromId && e.StreamOrdering <= currentId)
                .Take(limit)
                .OrderBy((e) => e.StreamOrdering).ToList())
            {
                var js = EventsJson.SingleOrDefault(e => e.EventId == ev.EventId);
                if (js != null)
                    set.Add(
                        new EventJsonSet(
                            ev,
                            js.Json,
                            js.FormatVersion
                        )
                    );
            }
            return set;
        }
    }
}
