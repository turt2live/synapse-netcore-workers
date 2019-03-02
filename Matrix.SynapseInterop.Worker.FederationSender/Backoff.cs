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
        private const int MaxMilliseconds = 60 * 60 * 24 * 1000;
        private readonly Dictionary<string, SBackoff> hosts;
        private readonly Random random;
        private static readonly TimeSpan HttpReqBackoff = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan NormalBackoff = TimeSpan.FromSeconds(30);

        public Backoff()
        {
            hosts = new Dictionary<string, SBackoff>();
            random = new Random();
        }

        public bool ClearBackoff(string host) => hosts.Remove(host);

        public TimeSpan GetBackoffForException(string host, Exception ex)
        {
            double multiplier = (double) random.Next(8, 14) / 10;

            if (!hosts.TryGetValue(host, out var backoff))
            {
                backoff = new SBackoff
                {
                    delayFor = TimeSpan.Zero,
                };

                hosts.Add(host, backoff);
            }

            if (ex is HttpRequestException || ex is JsonReaderException || ex is SocketException)
            {
                // This is a failure to route to the host, rather than a HTTP status code failure.
                // We want to harshly rate limit here, as the box may not host a synapse box.

                // Failing to parse the json is in the same category because it's usually a 404 page.

                // A socket exception also counts, because they are usually indicative of a remote host not being online.
                // We could also be suffering, which means we should probably backoff anyway.

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
                    backoff.delayFor = TimeSpan.FromMilliseconds(txEx.BackoffFor + 30000);
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
            else
            {
                // Eh, it's a error.
                backoff.delayFor += NormalBackoff * multiplier;
            }

            backoff.delayFor = TimeSpan.FromMilliseconds(Math.Min(MaxMilliseconds, backoff.delayFor.TotalMilliseconds));

            return backoff.delayFor;
        }
    }
}
