using System;
using System.Net.Http;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;
using Matrix.SynapseInterop.Common;

namespace Matrix.SynapseInterop.Worker.FederationSender
{
    public class FederationHttpClient : HttpClient
    {
        public FederationHttpClient(bool allowSelfSigned, TimeSpan connectTimeout,
                                    TimeSpan pooledConnectionIdleTimeout, TimeSpan pooledConnectionLifetime) : base(new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (sender, certificate, chain, sslpolicyerrors) => 
                    CheckCert(sslpolicyerrors, allowSelfSigned)
            },
            // FYI - defaults can be found at https://github.com/dotnet/corefx/blob/c0c370e0576574d8985970200c00ca83ae366d2e/src/Common/src/System/Net/Http/HttpHandlerDefaults.cs#L13
            UseProxy = false,
            UseCookies = false,
            ResponseDrainTimeout = TimeSpan.FromSeconds(15),
            ConnectTimeout = connectTimeout,
            PooledConnectionIdleTimeout = pooledConnectionIdleTimeout,
            PooledConnectionLifetime = pooledConnectionLifetime,
        })
        {
            Timeout = TimeSpan.FromMinutes(1);
        }
        
        private static bool CheckCert(SslPolicyErrors sslpolicyerrors,
                                      bool allowSelfSigned
        )
        {
            if (sslpolicyerrors.HasFlag(SslPolicyErrors.None)) return true;
        
            return sslpolicyerrors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch) &&
                   sslpolicyerrors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable) &&
                   allowSelfSigned;
        }

        public override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                WorkerMetrics.IncOngoingHttpConnections();
                var t = base.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                return t;
            }
            finally
            {
                WorkerMetrics.DecOngoingHttpConnections();
            }
        }
    }
}
