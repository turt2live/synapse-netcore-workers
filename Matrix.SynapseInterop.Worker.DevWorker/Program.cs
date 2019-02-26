using Matrix.SynapseInterop.Database;
using Matrix.SynapseInterop.Replication;
using Matrix.SynapseInterop.Replication.DataRows;
using Microsoft.Extensions.Configuration;
using System;
using System.Linq;

namespace Matrix.SynapseInterop.Worker.DevWorker
{
    class Program
    {
        private static IConfiguration _config;

        static void Main(string[] args)
        {
            _config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.default.json", true, true)
                .AddEnvironmentVariables()
                .AddCommandLine(args)
                .Build();

            StartReplicationAsync();

            Console.ReadKey(true);
        }

        private static async void StartReplicationAsync()
        {
            var replication = new SynapseReplication();
            replication.ClientName = "NetCoreDevWorker";
            replication.ServerName += Replication_ServerName;

            var synapseConfig = _config.GetSection("Synapse");
            await replication.Connect(synapseConfig.GetValue<string>("replicationHost"), synapseConfig.GetValue<int>("replicationPort"));

            var stream = replication.BindStream<EventStreamRow>();
            stream.DataRow += Stream_DataRow;
        }

        private static void Stream_DataRow(object sender, EventStreamRow e)
        {
            using (var db = new SynapseDbContext(_config.GetConnectionString("synapse")))
            {
                Console.WriteLine("Received event {0} ({1}) in {2}", e.EventId, e.EventType, e.RoomId);
                var ev = db.EventsJson.SingleOrDefault(e2 => e2.RoomId == e.RoomId && e2.EventId == e.EventId);
                if (ev != null)
                {
                    Console.WriteLine(ev.Json);
                }
                else
                {
                    Console.WriteLine("EVENT NOT FOUND");
                }
            }
        }

        private static void Replication_ServerName(object sender, string e)
        {
            Console.WriteLine("Server name: " + e);
        }
    }
}
