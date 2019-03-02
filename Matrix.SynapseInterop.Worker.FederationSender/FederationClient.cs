using System;
using System.Collections.Generic;
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

namespace Matrix.SynapseInterop.Worker.FederationSender
{
    class FederationClient
    {
        private SigningKey key;
        private HttpClient client;
        private Dictionary<string, Uri> destinationUris;
        private string origin;
        private bool allowSelfSigned;
        private HostResolver hostResolver;

        public FederationClient(string serverName, SigningKey key, IConfigurationSection config)
        {
            origin = serverName;

            client = new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = ServerCertificateValidationCallback,
            });

            client.Timeout = TimeSpan.FromSeconds(30);

            this.key = key;
            destinationUris = new Dictionary<string, Uri>();
            hostResolver = new HostResolver(config.GetValue<bool>("defaultToSecurePort") ? 8448 : 8008);
            allowSelfSigned = config.GetValue<bool>("allowSelfSigned");
        }

        private bool ServerCertificateValidationCallback(object sender,
                                                         X509Certificate certificate,
                                                         X509Chain chain,
                                                         SslPolicyErrors sslpolicyerrors
        )
        {
            if (sslpolicyerrors.HasFlag(SslPolicyErrors.None))
            {
                return true;
            }

            if (
                sslpolicyerrors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch) &&
                sslpolicyerrors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable) &&
                allowSelfSigned)
            {
                // XXX: Is this good enough to be considered self signed?
                // If self signed, then allow.
                return true;
            }

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
                RequestUri = uri.Uri,
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

            try
            {
                Console.WriteLine($"[TX] {transaction.destination} PUT {uri} PDUs={transaction.pdus.Count} EDUs={transaction.edus.Count}");
                resp = await client.SendAsync(msg);
            }
            catch (HttpRequestException ex)
            {
                //TODO: This is probably a little extreme.
                Console.WriteLine($"Failed to reach {transaction.destination} {ex.Message}");
                destinationUris.Remove(transaction.destination);
                throw;
            }

            Console.WriteLine($"[TX] {transaction.destination} Response: {resp.StatusCode} {resp.ReasonPhrase}");

            if (resp.IsSuccessStatusCode)
            {
                return;
            }

            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                destinationUris.Remove(transaction.destination);
            }
            // TODO: Should we drop well known for other reasons?

            string error = await resp.Content.ReadAsStringAsync();
            JObject err = JObject.Parse(error);

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Possible key fail, show some debug info for people to debug.
                try
                {
                    string errCode = (string) err["errcode"];
                    string errorString = (string) err["error"];

                    if (errCode == "M_UNAUTHORIZED" && errorString.StartsWith("Invalid signature"))
                    {
                        Console.WriteLine("Got invalid signature, debug info:");

                        Console.WriteLine("Auth: {0}\nBody: ${1}",
                                          msg.Headers.Authorization.Parameter,
                                          body.ToString(Formatting.Indented));
                    }
                }
                catch
                {
                    // ignored
                }
            }

            throw new TransactionFailureException(transaction.destination, resp.StatusCode, err);
        }

        private void SignRequest(HttpRequestMessage msg, string destination, JObject body = null)
        {
            var sigBody = new JObject();
            sigBody.Add(origin, new JObject());
            sigBody[origin].Value<JObject>().Add($"{key.Type}:${key.Name}", key.PublicKey);
            var signingBody = new JObject();

            if (body != null)
            {
                signingBody.Add("content", body);
            }

            signingBody.Add("destination", destination);
            signingBody.Add("method", msg.Method.Method.ToUpper());
            signingBody.Add("origin", origin);
            signingBody.Add("uri", msg.RequestUri.PathAndQuery);
            var signature = key.SignJson(signingBody);
            signingBody.Add("signatures", new JObject());
            (signingBody["signatures"] as JObject)?.Add(origin, new JObject());
            (signingBody["signatures"][origin] as JObject)?.Add($"{key.Type}:{key.Name}", signature);
            string authHeader = $"origin={origin},key=\"{key.Type}:{key.Name}\",sig=\"{signature}\"";

            msg.Headers.Authorization =
                new AuthenticationHeaderValue("X-Matrix",
                                              authHeader);
        }
    }
}
