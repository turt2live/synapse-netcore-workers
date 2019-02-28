using Matrix.SynapseInterop.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace Matrix.SynapseInterop.Database
{
    public class SynapseDbContext : DbContext
    {
        private readonly string _connString;

        public DbQuery<EventJson> Events { get; set; }

        public SynapseDbContext(string connectionString)
        {
            _connString = connectionString;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql(_connString);
        }
    }
}
