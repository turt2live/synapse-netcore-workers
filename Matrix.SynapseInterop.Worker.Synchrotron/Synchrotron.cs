using System.Threading.Tasks;
using Matrix.SynapseInterop.Replication;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;

namespace Matrix.SynapseInterop.Worker.Synchrotron
{
    public class Synchrotron
    {
        private ILogger _log = Log.ForContext<Synchrotron>();
        private SynapseReplication _synapseReplication;
        private string _connectionString;

        public async Task Start(IConfiguration _config)
        {
            _log.Information("Starting Synchrotron");

            _synapseReplication = new SynapseReplication();
            _synapseReplication.ClientName = "NetCoreSynchrotron";
            _synapseReplication.ServerName += SynapseReplicationOnServerName;

            var synapseConfig = _config.GetSection("Synapse");
            _connectionString = _config.GetConnectionString("synapse");

            await _synapseReplication.Connect(synapseConfig.GetValue<string>("replicationHost"),
                                              synapseConfig.GetValue<int>("replicationPort"));
            _synapseReplication.BindStream<Event>()
        }

        private void SynapseReplicationOnServerName(object sender, string e)
        {
            
        }
    }
}
