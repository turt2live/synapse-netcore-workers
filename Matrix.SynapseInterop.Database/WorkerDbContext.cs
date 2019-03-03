using Matrix.SynapseInterop.Database.WorkerModels;
using Microsoft.EntityFrameworkCore;

namespace Matrix.SynapseInterop.Database
{
    public class WorkerDbContext : DbContext
    {
        // We need to define a connection string for EF Core migrations to work
        private readonly string _connString =
            "Username=worker_user;Password=YourPasswordHere;Host=localhost;Database=dont_use_synapse;";

        public DbSet<Appservice> Appservices { get; set; }

        public WorkerDbContext(string connectionString)
        {
            if (!string.IsNullOrWhiteSpace(connectionString)) _connString = connectionString;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql(_connString);
        }
    }
}
