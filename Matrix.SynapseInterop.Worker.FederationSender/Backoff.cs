using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Matrix.SynapseInterop.Common;
using Newtonsoft.Json;

namespace Matrix.SynapseInterop.Worker.FederationSender
{
    internal struct SBackoff
    {
        public TimeSpan delayFor;
        public DateTime Ts;
        public bool isDown;
    }

    public class Backoff
    {
        private static readonly TimeSpan MaxDelay = TimeSpan.FromDays(1);
        private static readonly TimeSpan HttpReqBackoff = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan NormalBackoff = TimeSpan.FromSeconds(30);
        private readonly Dictionary<string, SBackoff> _hosts;
        private readonly Random random;

        public Backoff()
        {
            _hosts = new Dictionary<string, SBackoff>();
            random = new Random();
        }

        public bool ClearBackoff(string host) => _hosts.Remove(host);

        public bool HostIsDown(string host)
        {
            if (_hosts.TryGetValue(host, out var h))
            {
                return DateTime.Now < h.Ts + h.delayFor;
            }

            return false;
        }

        /// <summary>
        /// Should we mark the host as down?
        /// </summary>
        /// <param name="host">The host to check</param>
        /// <param name="ex"></param>
        /// <returns></returns>
        public bool MarkHostIfDown(string host, Exception ex)
        {
            var isDown = false;

            if (ex is SocketException sockEx)
            {
                if (sockEx.SocketErrorCode == SocketError.ConnectionRefused)
                {
                    // This is definitely us being rejected
                    isDown = true;
                }

                // Can't be sure who is at fault, play it safe.
            }
            else if (ex is HttpRequestException || ex is JsonReaderException || ex is OperationCanceledException)
            {
                // This is a failure to route to the host, rather than a HTTP status code failure.
                // Failing to parse the json is in the same category because it's usually a 404 page.
                // OperationCanceledException can also be thrown if the request times out.
                isDown = true;
            }
            else if (ex is TransactionFailureException txEx)
            {
                if (txEx.ErrorCode == "M_FORBIDDEN" && txEx.Error.StartsWith("Federation denied with"))
                {
                    // This means they don't want to federate with us :(
                    isDown = true;
                }
                else if (txEx.Code == HttpStatusCode.NotFound || txEx.Code == HttpStatusCode.BadGateway)
                    isDown = true;
            }
            
            if (_hosts.TryGetValue(host, out var h))
            {
                h.isDown = isDown;
                h.Ts = DateTime.Now;

                if (!isDown)
                {
                    h.delayFor = TimeSpan.Zero;
                }

                _hosts.Remove(host);
                _hosts.TryAdd(host, h);
            }
            else if (isDown)
            {
                _hosts.TryAdd(host, new SBackoff()
                {
                    isDown = true,
                    delayFor = TimeSpan.FromHours(5) + TimeSpan.FromMinutes(random.Next(0, 59)),
                    Ts = DateTime.Now,
                });
            }

            return isDown;
        }

        public TimeSpan GetBackoffForException(string host, Exception ex)
        {
            var multiplier = (double) random.Next(8, 16) / 10;

            if (!_hosts.TryGetValue(host, out var backoff))
            {
                backoff = new SBackoff
                {
                    delayFor = TimeSpan.Zero
                };
            }
            else
            {
                _hosts.Remove(host);
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

            _hosts.Add(host, backoff);

            backoff.delayFor = TimeSpan.FromMilliseconds(Math.Min(MaxDelay.TotalMilliseconds, backoff.delayFor.TotalMilliseconds));

            return backoff.delayFor;
        }
    }
}
