using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Threading.Tasks;
using DnsClient;
using Newtonsoft.Json.Linq;

namespace Matrix.SynapseInterop.Common
{
    public class HostRecord
    {
        private static readonly TimeSpan TTL = TimeSpan.FromDays(1);
        private Uri _resolvedUri;
        private readonly string _host;
        public DateTime LastAccessed;

        private ServiceHostEntry[] _entries;

        public HostRecord(Uri uri, ServiceHostEntry[] entries, string host)
        {
            _resolvedUri = uri;
            LastAccessed = DateTime.Now;
            _entries = entries;
            _host = host;
        }

        public Uri GetUri()
        {
            // TODO: Load balance
            return _resolvedUri;
        }

        public string GetHost()
        {
            if (_entries != null)
            {
                // This is spawned from a SRV record, use the destination.
                return _host;
            }
            return _resolvedUri.Host;
        }

        public bool Expired => (DateTime.Now - LastAccessed) > TTL;
    }

    public class HostResolver
    {
        private readonly Dictionary<string, HostRecord> _hosts;
        private readonly int _defaultPort;
        private readonly HttpClient _wKclient;
        private readonly LookupClient _lookupClient;

        public HostResolver(int defaultPort)
        {
            _wKclient = new HttpClient(new HttpClientHandler
            {
                // ServerCertificateCustomValidationCallback = ServerCertificateValidationCallback,
            }) {Timeout = TimeSpan.FromSeconds(15)};

            _hosts = new Dictionary<string, HostRecord>();
            _defaultPort = defaultPort;
            _lookupClient = new LookupClient();
        }

        public async Task<HostRecord> GetHostRecord(string destination)
        {
            bool hasValue = _hosts.TryGetValue(destination, out var host);
            bool expired = hasValue && host.Expired;

            if (hasValue && !expired)
            {
                return host;
            }

            WorkerMetrics.ReportCacheMiss("hostresolver_hosts");
            
            if (expired)
            {
                _hosts.Remove(destination);
            }

            using (WorkerMetrics.HostLookupDurationTimer())
            {
                var res = await ResolveHost(destination);
                host = new HostRecord(res.Item1, res.Item2, destination);
                _hosts.Add(destination, host);
            }
            
            WorkerMetrics.ReportCacheSize("hostresolver_hosts", _hosts.Count);

            return host;
        }

        private async Task<Tuple<Uri, ServiceHostEntry[]>> ResolveHost(string destination)
        {
            // https://matrix.org/docs/spec/server_server/r0.1.0.html#resolving-server-names
            if (TryHandleBasicHost(destination, out var basicHost))
            {
                return Tuple.Create<Uri, ServiceHostEntry[]>(basicHost, null);
            }

            var rawUri = CreateHostUri(destination);

            if (rawUri.HostNameType == UriHostNameType.Dns)
            {
                // 3. If the hostname is not an IP literal, a regular HTTPS request is made to https://<hostname>/.well-known/matrix/server
                try
                {
                    var res = await _wKclient.GetAsync($"https://{destination}/.well-known/matrix/server");
                    res.EnsureSuccessStatusCode();

                    var wellKnown = JObject.Parse(await res.Content.ReadAsStringAsync());

                    if (!wellKnown.ContainsKey("m.server"))
                    {
                        throw new Exception("No m.server key found");
                    }

                    var wellKnownHost = wellKnown["m.server"].ToObject<string>();

                    if (TryHandleBasicHost(wellKnownHost, out var wellKnownBasicHost))
                    {
                        return Tuple.Create<Uri, ServiceHostEntry[]>(wellKnownBasicHost, null);
                    }

                    // Well-known is valid but wasn't "basic", resolve it's DNS
                    rawUri = CreateHostUri(wellKnownHost);
                }
                catch (Exception)
                {
                    // ignored - Failures to resolve well known should fall through.
                }
            }

            // Do the DNS
            var dnsRes = await _lookupClient.ResolveServiceAsync(rawUri.Host, "matrix", ProtocolType.Tcp);

            if (dnsRes.Length == 0)
            {
                // No records, attempt to resolve the host directly.
                return Tuple.Create<Uri, ServiceHostEntry[]>(rawUri, null);
            }

            var host = dnsRes[0].HostName ?? rawUri.Host;
            
            rawUri = CreateHostUri($"{host}:{dnsRes[0].Port}");

            return Tuple.Create(rawUri, dnsRes);
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
