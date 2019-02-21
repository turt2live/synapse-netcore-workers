using System;
using System.Linq;
using System.Threading.Tasks;
using Matrix.SynapseInterop.Database;
using Matrix.SynapseInterop.Replication;
using Matrix.SynapseInterop.Replication.DataRows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Matrix.SynapseInterop.Worker.FederationSender
{
    public class FederationSender
    {
        private IConfiguration _config;
        private int _stream_position;
        private ReplicationStream<EventStreamRow> _eventStream;
        public FederationSender(IConfiguration config)
        {
            _config = config;
        }

        public async Task Start()
        {
            Console.WriteLine("Starting FederationWorker");
            _stream_position = await GetFederationPos("federation");
            var replication = new SynapseReplication();
            replication.ClientName = "NetCoreFederationWorker";
            replication.ServerName += Replication_ServerName;

            var synapseConfig = _config.GetSection("Synapse");
            await replication.Connect(synapseConfig.GetValue<string>("replicationHost"), synapseConfig.GetValue<int>("replicationPort"));
        
            var fedStream = replication.BindStream<FederationStreamRow>();
            fedStream.DataRow += OnFederationRow;
            _eventStream = replication.BindStream<EventStreamRow>();
            _eventStream.DataRow += OnEventRow;
        }
        
        private async Task<int> GetFederationPos(string type)
        {
            using (var db = new SynapseDbContext(_config.GetConnectionString("synapse")))
            {
                var query = db.FederationStreamPosition.Where((r) => r.Type == type);
                var res = await query.FirstOrDefaultAsync();
                return res?.StreamId ?? -1;
            }
        }
        
        private void UpdateFederationPos(string type, int id)
        {
            using (var db = new SynapseDbContext(_config.GetConnectionString("synapse")))
            {
                var query = db.FederationStreamPosition.Where((r) => r.Type == type);
                var res = query.First();
                res.StreamId = id;
            }
        }

        private async void OnFederationRow(object sender, FederationStreamRow e)
        {
            
            Console.WriteLine(e.GetHashCode());
        }
        
        private async void OnEventRow(object sender, EventStreamRow e)
        {
            var pos = await GetFederationPos("events");
            Console.WriteLine(e.GetHashCode());
        }

        private void Replication_ServerName(object sender, string e)
        {
            Console.WriteLine("Server name: " + e);
        }

        private void ProcessEventQueueLoop()
        {
            while (true) // This will be broken out of at some point.
            {
                
            }
        }
    }
}