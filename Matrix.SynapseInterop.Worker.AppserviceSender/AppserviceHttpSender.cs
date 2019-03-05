using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Matrix.SynapseInterop.Common.MatrixUtils;
using Matrix.SynapseInterop.Common.Transactions;
using Matrix.SynapseInterop.Database.WorkerModels;
using Matrix.SynapseInterop.Worker.AppserviceSender.Transactions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using Serilog.Core.Enrichers;

namespace Matrix.SynapseInterop.Worker.AppserviceSender
{
    public class AppserviceHttpSender
    {
        private readonly Appservice _appservice;
        private readonly HttpClient _client = new HttpClient();
        private readonly ILogger _log;

        public AppserviceHttpSender(Appservice appservice)
        {
            _appservice = appservice;

            _log = Log.ForContext<AppserviceHttpSender>()
                      .ForContext(new PropertyEnricher("appserviceId", appservice.Id));

            _client.DefaultRequestHeaders.Add("User-Agent", "NETCore Synapse Appservice Sender Worker");
            _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_appservice.HomeserverToken}");
        }

        public async Task<bool> SendTransaction(Transaction<QueuedEvent> txn)
        {
            var convertedEvents = txn.Elements.Select(e => e.Event.ToAppserviceFormat());

            var bodyObj = new JObject();
            bodyObj.Add("events", new JArray(convertedEvents));

            var jsonBody = new StringContent(JsonConvert.SerializeObject(bodyObj));
            jsonBody.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            var qs = $"?access_token={_appservice.HomeserverToken}";
            var uri = $"{_appservice.Url}/_matrix/app/v1/transactions/{txn.Id}{qs}";

            _log.Information("Sending transaction {0}", txn.Id);
            var resp = await _client.PutAsync(uri, jsonBody);

            // TODO: Fall back to /transactions/{0} on error

            _log.Information("Got status code {0} for transaction {1}", resp.StatusCode, txn.Id);
            return resp.StatusCode == HttpStatusCode.OK || resp.StatusCode == HttpStatusCode.NoContent;
        }
    }
}
