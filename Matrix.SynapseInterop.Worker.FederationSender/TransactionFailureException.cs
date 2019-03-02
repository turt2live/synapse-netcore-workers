using System;
using System.Net;
using Newtonsoft.Json.Linq;

namespace Matrix.SynapseInterop.Worker.FederationSender
{
    public class TransactionFailureException : Exception
    {
        public readonly HttpStatusCode Code;
        public readonly string Host;
        public readonly string ErrorCode;
        public readonly string Error;
        public readonly int BackoffFor = -1;

        public TransactionFailureException(string host, HttpStatusCode statusCode, JObject resp)
        {
            this.Host = host;
            Code = statusCode;

            if (resp.ContainsKey("retry_after_ms"))
            {
                BackoffFor = (int) resp["retry_after_ms"];
            }

            if (!resp.ContainsKey("errcode")) return;
            ErrorCode = (string) resp["errcode"];
            Error = (string) resp["error"];
        }

        public override string Message => $"Failure sending transaction to {Host}: {Code}. {ErrorCode} {Error}";
    }
}
