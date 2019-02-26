using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Matrix.SynapseInterop.Worker.FederationSender
{
    class FederationClient
    {
        private SigningKey key;
        private HttpClient client;
        private string origin;
        public FederationClient(string serverName, SigningKey key)
        {
            origin = serverName;
            client = new HttpClient();
            this.key = key;
        }

        public async Task SendTransaction(Transaction transaction)
        {
            var uri = new UriBuilder(GetUrlForDestination(transaction.destination));
            uri.Path += $"send/{transaction.transaction_id}/";
            Console.WriteLine($"[TX] PUT {uri} ");
            var msg = new HttpRequestMessage
            {
                Method = HttpMethod.Put,
                RequestUri = uri.Uri
            };
            var body = SigningKey.SortPropertiesAlphabetically(JObject.FromObject(transaction));
            SignRequest(msg, transaction.destination, body);
            var json = JsonConvert.SerializeObject(body, Formatting.None);
            var content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json"
            );
            msg.Content = content;
            var resp = await client.SendAsync(msg);
            Console.WriteLine($"[TX] Response: {resp.StatusCode} {resp.ReasonPhrase}");
            if (resp.IsSuccessStatusCode)
            {
                return;
            }
            string error = await resp.Content.ReadAsStringAsync();
            throw new Exception(error);
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
            msg.Headers.Authorization = new AuthenticationHeaderValue(
                "X-Matrix", $"origin={origin},key=\"{key.Type}:{key.Name}\",sig=\"{signature}\""
            );
        }

        private string GetUrlForDestination(string destination)
        {
            return $"http://{destination}:8008/_matrix/federation/v1/";
        }
    }
}