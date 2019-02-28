using Matrix.SynapseInterop.Database;

namespace Matrix.SynapseInterop.Worker.AppserviceSender
{
    public class AppserviceDb : WorkerDbContext
    {
        internal static string ConnectionString { get; set; }

        public AppserviceDb() : base(ConnectionString) { }
    }
}
