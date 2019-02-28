using Matrix.SynapseInterop.Database.WorkerModels;
using Microsoft.EntityFrameworkCore;

namespace Matrix.SynapseInterop.Database
{
    public class WorkerDbContext : DbContext
    {
        private readonly string _connString;
        // TODO: Migrations of some kind

        public DbQuery<Appservice> Appservices { get; set; }

        public WorkerDbContext(string connectionString)
        {
            _connString = connectionString;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql(_connString);
        }
    }
}
