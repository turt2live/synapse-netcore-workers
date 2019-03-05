using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Newtonsoft.Json;

namespace Matrix.SynapseInterop.Worker.FederationSender
{
    internal struct SBackoff
    {
        public TimeSpan delayFor;
    }

    public class Backoff
    {
        private static readonly TimeSpan MaxDelay = TimeSpan.FromDays(1);
        private static readonly TimeSpan HttpReqBackoff = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan NormalBackoff = TimeSpan.FromSeconds(30);
        private readonly Dictionary<string, SBackoff> hosts;
        private readonly Random random;

        public Backoff()
        {
            hosts = new Dictionary<string, SBackoff>();
            random = new Random();
        }

        public bool ClearBackoff(string host)
        {
            return hosts.Remove(host);
        }

        public TimeSpan GetBackoffForException(string host, Exception ex)
        {
            var multiplier = (double) random.Next(8, 16) / 10;

            if (!hosts.TryGetValue(host, out var backoff))
            {
                backoff = new SBackoff
                {
                    delayFor = TimeSpan.Zero
                };
            }
            else
            {
                hosts.Remove(host);
            }

            if (ex is HttpRequestException || ex is JsonReaderException || ex is SocketException)
            {
                // This is a failure to route to the host, rather than a HTTP status code failure.
                // We want to harshly rate limit here, as the box may not host a synapse box.

                // Failing to parse the json is in the same category because it's usually a 404 page.

                // A socket exception also counts, because they are usually indicative of a remote host not being online.
                // We could also be suffering, which means we should probably backoff anyway.
                return TimeSpan.Zero;
            }
            
            if (ex is TransactionFailureException txEx)
            {
                if (txEx.ErrorCode == "M_FORBIDDEN" && txEx.Error.StartsWith("Federation denied with"))
                {
                    // This means they don't want to federate with us :(
                    return TimeSpan.Zero;
                }
                
                if (txEx.Code == HttpStatusCode.NotFound)
                    return TimeSpan.Zero;

                if (txEx.Code == HttpStatusCode.BadGateway)
                    return TimeSpan.Zero;

                else if (txEx.BackoffFor > 0 && txEx.Code == HttpStatusCode.TooManyRequests)
                    backoff.delayFor = TimeSpan.FromMilliseconds(txEx.BackoffFor + 30000);
                else if (txEx.Code == HttpStatusCode.Unauthorized && txEx.Error != "")
                    // This is because the body is mangled and will never succeed, drop it.
                    return TimeSpan.Zero;
                else if (txEx.Code >= HttpStatusCode.InternalServerError)
                    backoff.delayFor += NormalBackoff * multiplier;
                else backoff.delayFor += NormalBackoff * multiplier;
            }
            else
            {
                // Eh, it's a error.
                backoff.delayFor += NormalBackoff * multiplier;
            }

            hosts.Add(host, backoff);

            backoff.delayFor = TimeSpan.FromMilliseconds(Math.Min(MaxDelay.TotalMilliseconds, backoff.delayFor.TotalMilliseconds));

            return backoff.delayFor;
        }
    }
}
