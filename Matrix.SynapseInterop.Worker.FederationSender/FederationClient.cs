using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
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
        private readonly bool allowSelfSigned;
        private readonly HttpClient client;
        private readonly HostResolver hostResolver;
        private readonly SigningKey key;
        private readonly string origin;

        public FederationClient(string serverName, SigningKey key, IConfigurationSection config)
        {
            origin = serverName;

            client = new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = ServerCertificateValidationCallback
            });

            client.Timeout = TimeSpan.FromMinutes(3);

            this.key = key;
            hostResolver = new HostResolver(config.GetValue<bool>("defaultToSecurePort") ? 8448 : 8008);
            allowSelfSigned = config.GetValue<bool>("allowSelfSigned");
        }

        private bool ServerCertificateValidationCallback(object sender,
                                                         X509Certificate certificate,
                                                         X509Chain chain,
                                                         SslPolicyErrors sslpolicyerrors
        )
        {
            if (sslpolicyerrors.HasFlag(SslPolicyErrors.None)) return true;

            if (
                sslpolicyerrors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch) &&
                sslpolicyerrors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable) &&
                allowSelfSigned) return true;

            return false;
        }

        public async Task SendTransaction(Transaction transaction)
        {
            var record = await hostResolver.GetHostRecord(transaction.destination);

            var uri = new UriBuilder(record.GetUri())
            {
                Path = $"/_matrix/federation/v1/send/{transaction.transaction_id}/",
                Scheme = "https"
            };

            var msg = new HttpRequestMessage
            {
                Method = HttpMethod.Put,
                RequestUri = uri.Uri
            };

            msg.Headers.Host = record.GetHost();

            var body = SigningKey.SortPropertiesAlphabetically(JObject.FromObject(transaction));
            SignRequest(msg, transaction.destination, body);
            var json = JsonConvert.SerializeObject(body, Formatting.None);

            var content = new StringContent(json,
                                            Encoding.UTF8,
                                            "application/json");

            msg.Content = content;
            HttpResponseMessage resp;
            var sw = new Stopwatch();

            try
            {
                log.Information("[TX] {destination} PUT {uri} Host={hostHeader} PDUs={pduCount} EDUs={eduCount}"
                                , transaction.destination, uri, msg.Headers.Host, transaction.pdus.Count,
                                transaction.edus.Count);

                sw.Start();
                resp = await client.SendAsync(msg);
            }
            catch (HttpRequestException ex)
            {
                //TODO: This is probably a little extreme.
                log.Warning("Failed to reach {destination} {message}", transaction.destination, ex.Message);
                throw;
            }
            finally
            {
                sw.Stop();
            }

            log.Information("[TX] {destination} Response: {statusCode} {timeTaken}ms",
                            transaction.destination, resp.StatusCode, sw.ElapsedMilliseconds);

            if (resp.IsSuccessStatusCode) return;

            if (resp.StatusCode == HttpStatusCode.NotFound) destinationUris.Remove(transaction.destination);
            // TODO: Should we drop well known for other reasons?

            var error = await resp.Content.ReadAsStringAsync();
            var err = JObject.Parse(error);

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
                try
                {
                    var errCode = (string) err["errcode"];
                    var errorString = (string) err["error"];

                    if (errCode == "M_UNAUTHORIZED" && errorString.StartsWith("Invalid signature"))
                    {
                        log.Information("Got invalid signature, debug info:");

                        log.Information("Auth: {auth}\nBody: {body}",
                                        msg.Headers.Authorization.Parameter,
                                        body.ToString(Formatting.Indented));
                    }
                }
                catch
                {
                    // ignored
                }

            throw new TransactionFailureException(transaction.destination, resp.StatusCode, err);
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
