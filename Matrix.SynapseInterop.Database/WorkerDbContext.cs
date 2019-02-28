using Matrix.SynapseInterop.Database.WorkerModels;
using Microsoft.EntityFrameworkCore;

namespace Matrix.SynapseInterop.Database
{
    public class WorkerDbContext : DbContext
    {
        // TODO: Migrations of some kind

        public DbQuery<Appservice> Appservices { get; set; }

        private string _connString;

        public WorkerDbContext(string connectionString)
        {
            this._connString = connectionString;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql(_connString);
        }
    }
}
