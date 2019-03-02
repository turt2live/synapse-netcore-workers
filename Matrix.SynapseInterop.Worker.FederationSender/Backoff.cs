using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Newtonsoft.Json;

namespace Matrix.SynapseInterop.Worker.FederationSender
{
    internal struct SBackoff
    {
        public TimeSpan delayFor;
    }
    
    public class Backoff
    {
        private const int MAX_MILLISECONDS = 60 * 60 * 24 * 1000;
        private Dictionary<string, SBackoff> hosts;
        private Random random;
        private static readonly TimeSpan HttpReqBackoff = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan NormalBackoff = TimeSpan.FromSeconds(30);

        public Backoff()
        {
            hosts = new Dictionary<string, SBackoff>();
            random = new Random();
        }

        public bool ClearBackoff(string host) => hosts.Remove(host);

        public TimeSpan GetBackoffForException(string host, Exception ex)
        {
            double multiplier = random.NextDouble() + 0.5;

            if (!hosts.TryGetValue(host, out var backoff))
            {
                backoff = new SBackoff
                {
                    delayFor = TimeSpan.Zero,
                };

                hosts.Add(host, backoff);
            }
            
            if (ex is HttpRequestException)
            {
                // This is a failure to route to the host, rather than a HTTP status code failure.
                // We want to harshly rate limit here, as the box may not host a synapse box.
                backoff.delayFor += HttpReqBackoff * multiplier;
            }
            else if (ex is JsonReaderException)
            {
                // This is an error that failed to parse. Give them a large backoff.
                backoff.delayFor += HttpReqBackoff * multiplier;
            }
            else if (ex is TransactionFailureException txEx)
            {
                if (txEx.Code == HttpStatusCode.NotFound)
                {
                    backoff.delayFor += HttpReqBackoff * multiplier;
                }
                else if (txEx.BackoffFor > 0 && txEx.Code == HttpStatusCode.TooManyRequests)
                {
                    backoff.delayFor = TimeSpan.FromMilliseconds(txEx.BackoffFor + 500);
                }
                else if (txEx.Code == HttpStatusCode.Unauthorized && txEx.Error != "")
                {
                    // These usually happen because a remote server didn't like our keys :(
                    backoff.delayFor += NormalBackoff * multiplier;
                }
                else if (txEx.Code >= HttpStatusCode.InternalServerError)
                {
                    // These just happen, and synapse usually doesn't tell us why.
                    backoff.delayFor += NormalBackoff * multiplier;
                }
                else
                {
                    // Eh, it's a error.
                    backoff.delayFor += NormalBackoff * multiplier;
                }
            }

            backoff.delayFor = TimeSpan.FromMilliseconds(Math.Min(MAX_MILLISECONDS, backoff.delayFor.TotalMilliseconds));
            
            return backoff.delayFor;
        }
    }
}
