using System;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using Matrix.SynapseInterop.Database;
using Matrix.SynapseInterop.Replication;
using Matrix.SynapseInterop.Replication.DataRows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Matrix.SynapseInterop.Worker.FederationSender
{
    public class FederationSender
    {
        private static readonly ILogger log = Log.ForContext<FederationSender>();
        private readonly IConfiguration _config;
        private ReplicationStream<EventStreamRow> _eventStream;
        private ReplicationStream<FederationStreamRow> _fedStream;
        private ReplicationStream<ReceiptStreamRow> _receiptStream;
        private int _last_ack;
        private bool _presenceEnabled;
        private int _stream_position;
        private SynapseReplication _synapseReplication;
        private TransactionQueue _transactionQueue;
        private string connectionString;
        private SigningKey key;
        private Timer saveFedToken;

        public FederationSender(IConfiguration config)
        {
            _config = config;
            _last_ack = -1;
        }

        public async Task Start()
        {
            log.Information("Starting FederationWorker");
            _synapseReplication = new SynapseReplication();
            _synapseReplication.ClientName = "NetCoreFederationWorker";
            _synapseReplication.ServerName += Replication_ServerName;

            var synapseConfig = _config.GetSection("Synapse");
            key = SigningKey.ReadFromFile(synapseConfig.GetValue<string>("signingKeyPath"));
            connectionString = _config.GetConnectionString("synapse");
            _presenceEnabled = synapseConfig.GetValue("presenceEnabled", true);

            await _synapseReplication.Connect(synapseConfig.GetValue<string>("replicationHost"),
                                              synapseConfig.GetValue<int>("replicationPort"));

            _fedStream = _synapseReplication.BindStream<FederationStreamRow>();
            _fedStream.DataRow += OnFederationRow;
            _eventStream = _synapseReplication.BindStream<EventStreamRow>();
            _eventStream.PositionUpdate += OnEventPositionUpdate;
            _stream_position = await GetFederationPos("federation");
            
            // Newer versions of synapse expect us to create receipts too.
            if (_config.GetSection("federation").GetValue<bool>("handleReceipts"))
            {
                _receiptStream = _synapseReplication.BindStream<ReceiptStreamRow>();
                _receiptStream.DataRow += OnReceiptRow;
            }
          
            saveFedToken = new Timer(150)
            {
                AutoReset = false,
                Enabled = false,
            };
            
            // Doing this avoids us making repeated calls to save the federation.
            saveFedToken.Elapsed += (sender, args) =>
            {
                UpdateFederationPos("federation", _stream_position);
            };
        }

        private void OnReceiptRow(object sender, ReceiptStreamRow e)
        {
            _transactionQueue?.OnReceipt(e);
        }

        private async Task<int> GetFederationPos(string type)
        {
            using (var db = new SynapseDbContext(connectionString))
            {
                var query = db.FederationStreamPosition.Where(r => r.Type == type);
                var res = await query.FirstOrDefaultAsync();
                return res?.StreamId ?? -1;
            }
        }

        private void UpdateFederationPos(string type, int id)
        {
            lock (this)
            {
                using (var db = new SynapseDbContext(connectionString))
                {
                    log.Information("Saving {type} position {pos}", type, id);
                    var res = db.FederationStreamPosition.SingleOrDefault(r => r.Type == type);
                    if (res == null) return;
                    res.StreamId = id;
                    db.SaveChanges();
                }
            }
        }

        private void OnFederationRow(object sender, FederationStreamRow e)
        {
            try
            {
                if (_presenceEnabled && e.presence.Count != 0) _transactionQueue.SendPresence(e.presence);

                e.edus.ForEach(_transactionQueue.SendEdu);

                foreach (var keyVal in e.keyedEdus)
                    _transactionQueue.SendEdu(keyVal.Value,
                                              keyVal.Key.Join(":"));
                
                if (e.devices.Count > 0)
                    log.Debug("Prodded device updates for {dests}", e.devices.Join(", "));

                e.devices.ForEach(_transactionQueue.SendDeviceMessages);
                UpdateToken(int.Parse(_fedStream.CurrentPosition));
            }
            catch (Exception ex)
            {
                log.Warning("Failed to handle transaction, got {ex}", ex);
            }
        }

        private void OnEventPositionUpdate(object sender, string stream_pos)
        {
            _transactionQueue?.OnEventUpdate(stream_pos);
        }

        private void Replication_ServerName(object sender, string serverName)
        {
            log.Information("Server name: {serverName}", serverName);

            _transactionQueue = new TransactionQueue(serverName,
                                                     connectionString,
                                                     key,
                                                     _config.GetSection("Federation"));
        }

        private void UpdateToken(int token)
        {
            _stream_position = token;

            if (_last_ack >= _stream_position) return;

            try
            {
                _synapseReplication.SendFederationAck(_stream_position.ToString());
            }
            catch (Exception ex)
            {
                // TODO: Is this correct? We update the token in the DB so
                // a restarted master should be okay, but it might also mean that
                // we might see duplicate traffic on connection loss.
                log.Error("Failed to ACK federation, dropping. {ex}", ex);
            }
            
            _last_ack = token;
            saveFedToken.Stop();
            saveFedToken.Start();
        }
    }
}
