using Matrix.SynapseInterop.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace Matrix.SynapseInterop.Database
{
    public class SynapseDbContext : DbContext
    {
        public DbQuery<EventJson> Events { get; set; }
        public DbQuery<FederationStreamPosition> FederationStreamPosition { get; set; }

        private string _connString;

        public SynapseDbContext(string connectionString)
        {
            this._connString = connectionString;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql(_connString);
        }
    }
}
