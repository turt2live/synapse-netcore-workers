using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Matrix.SynapseInterop.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace Matrix.SynapseInterop.Database
{
    public class SynapseDbContext : DbContext
    {
        public DbQuery<EventJson> EventsJson { get; set; }
        public DbQuery<Event> Events { get; set; }
        public DbQuery<EventForwardExtremities> EventForwardExtremities { get; set; }
        public DbQuery<RoomMemberships> RoomMemberships { get; set; }
        public DbSet<FederationStreamPosition> FederationStreamPosition { get; set; }

        private string _connString;

        public SynapseDbContext(string connectionString)
        {
            this._connString = connectionString;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql(_connString);
        }

        public List<EventJsonSet> GetAllNewEventsStream(int fromId, int currentId, int limit)
        {
            List<EventJsonSet> set = new List<EventJsonSet>();
            foreach (var ev in Events
                .Where(e => e.StreamOrdering < fromId && e.StreamOrdering <= currentId)
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
