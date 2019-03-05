using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using DnsClient.Protocol;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Matrix.SynapseInterop.Common
{
    public enum HostStatus
    {
        UNKNOWN = 0,
        UP = 1,
        DOWN = 2,
    }
    
    public class HostRecord
    {
        private static readonly TimeSpan TTL = TimeSpan.FromHours(6);

        private readonly SrvRecord[] _entries;
        private readonly string _host;
        private readonly Uri _resolvedUri;
        private HostStatus status;
        public DateTime LastAccessed;

        public bool Expired => DateTime.Now - LastAccessed > TTL;

        public HostRecord(Uri uri, SrvRecord[] entries, string host)
        {
            _resolvedUri = uri;
            LastAccessed = DateTime.Now;
            _entries = entries;
            _host = host;
            status = HostStatus.UNKNOWN;
        }

        public Uri GetUri()
        {
            // TODO: Load balance
            return _resolvedUri;
        }

        public void SetStatus(HostStatus s)
        {
            status = s;
        }
        
        public HostStatus GetStatus()
        {
            return status;
        }
        
        public string GetHost()
        {
            if (_entries != null) return _host;

            return _resolvedUri.Host;
        }
    }

    public class HostResolver
    {
        private static readonly ILogger log = Log.ForContext<HostResolver>();
        private readonly int _defaultPort;
        private readonly Dictionary<string, HostRecord> _hosts;
        private readonly LookupClient _lookupClient;
        private readonly HttpClient _wKclient;
        private SemaphoreSlim _srvSemaphore;

        public HostResolver(int defaultPort)
        {
            _wKclient = new HttpClient(new HttpClientHandler()) {Timeout = TimeSpan.FromSeconds(15)};

            _hosts = new Dictionary<string, HostRecord>();
            _defaultPort = defaultPort;
            _lookupClient = new LookupClient();
            _srvSemaphore = new SemaphoreSlim(500, 500);
        }

        public async Task<HostRecord> GetHostRecord(string destination)
        {
            var hasValue = _hosts.TryGetValue(destination, out var host);

            if (hasValue && !host.Expired)
            {
                WorkerMetrics.ReportCacheHit("hostresolver_hosts");
                return host;
            }

            WorkerMetrics.ReportCacheMiss("hostresolver_hosts");

            if (hasValue) _hosts.Remove(destination);

            using (WorkerMetrics.HostLookupDurationTimer())
            {
                var res = await ResolveHost(destination);
                host = new HostRecord(res.Item1, res.Item2, destination);
                _hosts.Add(destination, host);
            }

            WorkerMetrics.ReportCacheSize("hostresolver_hosts", _hosts.Count);
            
            // Test if the host is down.

            return host;
        }

        public void RemovehostRecord(string destination) => _hosts.Remove(destination);

        private async Task<Tuple<Uri, SrvRecord[]>> ResolveHost(string destination)
        {
            log.Debug("Resolving host {host}", destination);

            // https://matrix.org/docs/spec/server_server/r0.1.0.html#resolving-server-names
            if (TryHandleBasicHost(destination, out var basicHost))
                return Tuple.Create<Uri, SrvRecord[]>(basicHost, null);

            var rawUri = CreateHostUri(destination);

            if (rawUri.HostNameType == UriHostNameType.Dns)
                try
                {
                    var res = await _wKclient.GetAsync($"https://{destination}/.well-known/matrix/server");
                    res.EnsureSuccessStatusCode();

                    var wellKnown = JObject.Parse(await res.Content.ReadAsStringAsync());

                    if (!wellKnown.ContainsKey("m.server")) throw new Exception("No m.server key found");

                    var wellKnownHost = wellKnown["m.server"].ToObject<string>();

                    if (TryHandleBasicHost(wellKnownHost, out var wellKnownBasicHost))
                        return Tuple.Create<Uri, SrvRecord[]>(wellKnownBasicHost, null);

                    // Well-known is valid but wasn't "basic", resolve it's DNS
                    rawUri = CreateHostUri(wellKnownHost);
                }
                catch (Exception)
                {
                    // ignored - Failures to resolve well known should fall through.
                }

            // Do the DNS - Wait here to ensure we don't spam the DNS
            await _srvSemaphore.WaitAsync();

            var result = await _lookupClient
               .QueryAsync($"_matrix._tcp.{rawUri.Host}", QueryType.SRV);

            _srvSemaphore.Release();
            var records = result.Answers.Where(r => r is SrvRecord).Cast<SrvRecord>().ToArray();

            if (result.HasError || records.Length == 0) return Tuple.Create<Uri, SrvRecord[]>(rawUri, null);

            rawUri = CreateHostUri($"{records[0].Target.Value}:{records[0].Port}");

            return Tuple.Create(rawUri, records);
        }

        private bool TryHandleBasicHost(string destination, out Uri uri)
        {
            // We have to set a schema or Uri gets upset.
            var rawUri = CreateHostUri(destination);

            if (rawUri.HostNameType == UriHostNameType.IPv4 || rawUri.HostNameType == UriHostNameType.IPv6)
            {
                // 1. If the hostname is an IP literal, then that IP address should be used, together with the given port number, or 8448 if no port is given.
                uri = rawUri;
                return true;
            }

            // We can't actually ask for the given port (if any), so we check if it's the default port, but our string does not include a port.
            if (rawUri.HostNameType == UriHostNameType.Dns && rawUri.Port == 8448 && destination.EndsWith(":8448"))
            {
                // 2. If the hostname is not an IP literal, and the server name includes an explicit port, resolve the IP address using AAAA or A records.
                uri = rawUri;
                return true;
            }

            uri = null;
            return false;
        }

        private Uri CreateHostUri(string destination)
        {
            var rawUri = new UriBuilder("https://" + destination);
            rawUri.Port = rawUri.Port == 443 && !destination.EndsWith(":443") ? _defaultPort : rawUri.Port;
            return rawUri.Uri;
        }
    }
}
