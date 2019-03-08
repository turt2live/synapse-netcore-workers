using System;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Matrix.SynapseInterop.Common;

namespace Matrix.SynapseInterop.Worker.FederationSender
{
    public class FederationHttpClient : HttpClient
    {
        public FederationHttpClient(bool allowSelfSigned) : base(new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (sender, certificate, chain, sslpolicyerrors) => 
                    CheckCert(sender, certificate, chain, sslpolicyerrors, allowSelfSigned)
            },
            UseProxy = false,
            UseCookies = false,
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromSeconds(15)
        })
        {
            Timeout = TimeSpan.FromMinutes(1);
        }
        
        private static bool CheckCert(object sender,
                                      X509Certificate certificate,
                                      X509Chain chain,
                                      SslPolicyErrors sslpolicyerrors,
                                      bool allowSelfSigned
        )
        {
            if (sslpolicyerrors.HasFlag(SslPolicyErrors.None)) return true;
        
            return sslpolicyerrors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch) &&
                   sslpolicyerrors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable) &&
                   allowSelfSigned;
        }

        public override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            WorkerMetrics.IncOngoingHttpConnections();
            var res = await base.SendAsync(request, cancellationToken);
            WorkerMetrics.DecOngoingHttpConnections();
            return res;
        }
    }
}
