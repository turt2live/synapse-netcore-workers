using System;
using System.Net;
using Newtonsoft.Json.Linq;

namespace Matrix.SynapseInterop.Worker.FederationSender
{
    public class TransactionFailureException : Exception
    {
        public readonly int BackoffFor = -1;
        public readonly HttpStatusCode Code;
        public readonly string Error;
        public readonly string ErrorCode;
        public readonly string Host;

        public override string Message => $"Failure sending transaction to {Host}: {Code}. {ErrorCode} {Error}";

        public TransactionFailureException(string host, HttpStatusCode statusCode, JObject resp = null)
        {
            Host = host;
            Code = statusCode;
            if (resp == null ) return;

            if (resp.ContainsKey("retry_after_ms")) BackoffFor = (int) resp["retry_after_ms"];

            ErrorCode = (string) resp["errcode"];
            Error = (string) resp["error"];
        }
    }
}
