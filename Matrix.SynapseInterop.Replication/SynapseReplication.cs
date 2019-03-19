using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Matrix.SynapseInterop.Replication.DataRows;
using Serilog;

namespace Matrix.SynapseInterop.Replication
{
    public class SynapseReplication
    {
        private static readonly ILogger log = Log.ForContext<SynapseReplication>();

        private readonly Dictionary<string, ReplicationData>
            _pendingBatches = new Dictionary<string, ReplicationData>();

        private readonly Dictionary<Type, object>
            _streams = new Dictionary<Type, object>(); // object is a ReplicationStream<T>. TODO: Don't do this.

        private TcpClient _client;
        private string _lastAddress;
        private int _lastPort;
        private Timer _pingTimer;

        public string ClientName { get; set; }

        public event EventHandler<string> ServerName;
        public event EventHandler<ReplicationData> RData;
        public event EventHandler<StreamPosition> PositionUpdate;
        public event EventHandler<string> Error;
        public event EventHandler<string> Ping;
        public event EventHandler Connected; // Fired for reconnections too

        public async Task Connect(string address, int port)
        {
            _lastAddress = address;
            _lastPort = port;

            await Reconnect(true);
        }

        public void Disconnect()
        {
            _client?.Close();

            // Stop the timer - we'll create a new one if we need to
            _pingTimer?.Change(Timeout.Infinite, Timeout.Infinite);

            _pendingBatches.Clear();
        }

        public async Task Reconnect(bool firstAttempt = false)
        {
            if (!firstAttempt) log.Information("Lost replication connection - reconnecting");

            Disconnect();

            // Resolve the address
            var dns = await Dns.GetHostEntryAsync(_lastAddress);
            IPAddress ip;

            try
            {
                // TcpClient doesn't support IPV6 :(
                ip = dns.AddressList.First(i => i.AddressFamily == AddressFamily.InterNetwork);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException($"No IPv4 address found for {_lastAddress}", ex);
            }

            // Form a connection
            _client = new TcpClient();
            log.Information("Connecting to replication stream on {ip}:{_lastPort}", ip, _lastPort);
            await _client.ConnectAsync(ip, _lastPort);

            // Name our client
            var name = string.IsNullOrWhiteSpace(ClientName) ? "NETCORESynapseReplication" : ClientName;
            SendRaw("NAME " + name);

            // Start pinging 
            _pingTimer = new Timer(SendPing, null, TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(5));

            // Start the reader
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(() => ReadLoop());
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

            Connected?.Invoke(this, null);
        }

        private async Task ReconnectLoop()
        {
            var attempt = 1;

            while (true)
            {
                log.Information($"Reconnecting to replication: Attempt {attempt++}");

                try
                {
                    await Reconnect();
                    break;
                }
                catch (Exception ex)
                {
                    log.Error($"Reconnection failed: {ex}");
                    await Task.Delay(TimeSpan.FromSeconds(5));
                }
            }
        }

        private async void ReadLoop()
        {
            try
            {
                while (_client.Connected)
                {
                    var buf = new byte[1024];
                    var stream = _client.GetStream();

                    var result = new StringBuilder();

                    do
                    {
                        var read = stream.Read(buf, 0, buf.Length);
                        result.Append(Encoding.UTF8.GetString(buf, 0, read));
                    } while (stream.DataAvailable);

                    try
                    {
                        ProcessCommands(result.ToString());
                    }
                    catch (Exception ex)
                    {
                        log.Error("Failed to process command: {0}", ex);
                        SendRaw("ERROR Error processing incoming commands");
                        await Reconnect();
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error("Failed to read from replication: {0}", ex);
                ReconnectLoop().Wait();
            }
        }

        private void ProcessCommands(string raw)
        {
            var byLine = raw.Split('\n').Where(c => !string.IsNullOrWhiteSpace(c));

            foreach (var cmd in byLine)
                if (cmd.StartsWith("SERVER "))
                {
                    if (ServerName == null) continue;
                    ServerName(this, cmd.Substring("SERVER ".Length));
                }
                else if (cmd.StartsWith("RDATA "))
                {
                    if (RData == null) continue;
                    var row = cmd.Substring("RDATA ".Length);
                    var rowParts = row.Split(new[] {' '}, 3);

                    var stream = rowParts[0];
                    var position = rowParts[1];
                    var rowData = rowParts[2];

                    if (!_pendingBatches.ContainsKey(stream)) _pendingBatches.Add(stream, new ReplicationData(stream));
                    _pendingBatches[stream].AppendRow(rowData);

                    if (position != "batch")
                    {
                        var rdata = _pendingBatches[stream];
                        rdata.Position = position;
                        _pendingBatches.Remove(stream);
                        RData(this, rdata);
                    }
                }
                else if (cmd.StartsWith("POSITION "))
                {
                    if (PositionUpdate == null) continue;
                    var posParts = cmd.Substring("POSITION ".Length).Split(new[] {' '}, 2);

                    var stream = posParts[0];
                    var position = posParts[1];

                    PositionUpdate(this, new StreamPosition {StreamName = stream, Position = position});
                }
                else if (cmd.StartsWith("PING "))
                {
                    if (Ping == null) continue;
                    Ping(this, cmd.Substring("PING ".Length));
                }
                else if (cmd.StartsWith("ERROR "))
                {
                    if (Error == null) continue;
                    Error(this, cmd.Substring("ERROR ".Length));
                }
        }

        public void SendRaw(string command)
        {
            var shortCommand = command.Length > 80 ? command.Substring(0, 80) : command;
            log.Debug($"Sending {shortCommand}");

            try
            {
                _client.Client.Send(Encoding.UTF8.GetBytes(command + "\n"));
            }
            catch (Exception ex)
            {
                ReconnectLoop().Wait();
            }
        }

        private void SendPing(object context)
        {
            SendRaw("PING " + DateTime.Now.ToBinary());
        }

        public void SubscribeStream(string streamName, string position)
        {
            SendRaw("REPLICATE " + streamName + " " + position);
        }

        public void SendFederationAck(string token)
        {
            SendRaw($"FEDERATION_ACK {token}");
        }

        public ReplicationStream<T> BindStream<T>() where T : IReplicationDataRow
        {
            if (!_streams.ContainsKey(typeof(T))) ResumeStream<T>(StreamPosition.LATEST);
            return (ReplicationStream<T>) _streams[typeof(T)];
        }

        public ReplicationStream<T> ResumeStream<T>(string fromPosition) where T : IReplicationDataRow
        {
            if (_streams.ContainsKey(typeof(T))) throw new ArgumentException("A stream has already been started");
            _streams.Add(typeof(T), new ReplicationStream<T>(this, fromPosition));
            return (ReplicationStream<T>) _streams[typeof(T)];
        }
    }
}
