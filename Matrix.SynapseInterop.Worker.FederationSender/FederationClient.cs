using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Matrix.SynapseInterop.Common;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Matrix.SynapseInterop.Worker.FederationSender
{
    internal class FederationClient
    {
        private static readonly ILogger log = Log.ForContext<FederationSender>();
        private FederationHttpClient client;
        private readonly HostResolver hostResolver;
        private readonly SigningKey key;
        private readonly string origin;

        public FederationClient(string serverName, SigningKey key, IConfigurationSection config)
        {
            origin = serverName;

            this.key = key;
            hostResolver = new HostResolver(config.GetValue<bool>("defaultToSecurePort") ? 8448 : 8008);
            client = new FederationHttpClient(config.GetValue<bool>("allowSelfSigned", false));
        }
        
        public async Task SendTransaction(Transaction transaction)
        {
            var record = await hostResolver.GetHostRecord(transaction.Destination);

            var uri = new UriBuilder(record.GetUri())
            {
                Path = $"/_matrix/federation/v1/send/{transaction.TxnId}/",
                Scheme = "https"
            };

            var msg = new HttpRequestMessage
            {
                Method = HttpMethod.Put,
                RequestUri = uri.Uri
            };

            msg.Headers.Host = record.GetHost();

            var body = SigningKey.SortPropertiesAlphabetically(JObject.FromObject(transaction));
            SignRequest(msg, transaction.Destination, body);
            var json = JsonConvert.SerializeObject(body, Formatting.None);

            var content = new StringContent(json,
                                            Encoding.UTF8,
                                            "application/json");

            msg.Content = content;

            log.Debug("[TX] {destination} => /send/{txnId} PDUs={pduCount} EDUs={eduCount}"
                            , transaction.Destination, transaction.TxnId, transaction.pdus.Count,
                            transaction.edus.Count);

            HttpResponseMessage resp = await Send(msg, transaction.Destination);

            if (resp.IsSuccessStatusCode)
            {
                resp.Content.Dispose();
                return;
            }

            var error = await resp.Content.ReadAsStringAsync();
            var err = JObject.Parse(error);

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                try
                {
                    var errCode = (string) err["errcode"];
                    var errorString = (string) err["error"];

                    if (errCode == "M_UNAUTHORIZED" && errorString.StartsWith("Invalid signature"))
                    {
                        log.Warning("Got invalid signature");

                        log.Debug("Auth: {auth}\nBody: {body}",
                                        msg.Headers.Authorization.Parameter,
                                        body.ToString(Formatting.Indented));
                    }
                }
                catch
                {
                    // ignored
                }
            }

            throw new TransactionFailureException(transaction.Destination, resp.StatusCode, err);
        }

        private async Task<JObject> GetVersion(string destination)
        {
            var record = await hostResolver.GetHostRecord(destination);

            var msg = new HttpRequestMessage
            {
                Method = HttpMethod.Put,
                RequestUri = new UriBuilder(record.GetUri())
                {
                    Path = $"/_matrix/federation/version",
                    Scheme = "https",
                }.Uri,
            };

            msg.Headers.Host = record.GetHost();

            var res = await Send(msg, destination);
            res.EnsureSuccessStatusCode();
            return JObject.Parse(await res.Content.ReadAsStringAsync());
        }

        private async Task<HttpResponseMessage> Send(HttpRequestMessage msg, string destination)
        {
            var sw = new Stopwatch();
            HttpResponseMessage resp = null;

            try
            {
                log.Debug("[TX] {destination} PUT {uri} Host={hostHeader}"
                                , destination, msg.RequestUri, msg.Headers.Host);

                sw.Start();
                resp = await client.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);
            }
            catch (HttpRequestException ex)
            {
                //TODO: This is probably a little extreme.
                log.Warning("Failed to reach {destination} {message}", destination, ex.Message);
                hostResolver.RemovehostRecord(destination);
                throw;
            }
            finally
            {
                sw.Stop();

                log.Debug("[TX] {destination} Response {timeTaken}ms {statusCode} ",
                                destination, sw.ElapsedMilliseconds, resp?.StatusCode);
            }
            if (resp != null && resp.StatusCode == HttpStatusCode.NotFound)
            {
                hostResolver.RemovehostRecord(destination);
            }

            return resp;
        }

        private void SignRequest(HttpRequestMessage msg, string destination, JObject body = null)
        {
            var sigBody = new JObject();
            sigBody.Add(origin, new JObject());
            sigBody[origin].Value<JObject>().Add($"{key.Type}:${key.Name}", key.PublicKey);
            var signingBody = new JObject();

            if (body != null) signingBody.Add("content", body);

            signingBody.Add("destination", destination);
            signingBody.Add("method", msg.Method.Method.ToUpper());
            signingBody.Add("origin", origin);
            signingBody.Add("uri", msg.RequestUri.PathAndQuery);
            var signature = key.SignJson(signingBody);
            signingBody.Add("signatures", new JObject());
            (signingBody["signatures"] as JObject)?.Add(origin, new JObject());
            (signingBody["signatures"][origin] as JObject)?.Add($"{key.Type}:{key.Name}", signature);
            var authHeader = $"origin={origin},key=\"{key.Type}:{key.Name}\",sig=\"{signature}\"";

            msg.Headers.Authorization =
                new AuthenticationHeaderValue("X-Matrix",
                                              authHeader);
        }
    }
}
