using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace TorVps.Core.Services;

/// <summary>
/// Builds an <see cref="HttpClient"/> whose TLS is performed by a managed stack (BouncyCastle) instead of the OS.
/// </summary>
/// <remarks>
/// The Glances monitor endpoint is TLS 1.3-only. On Windows 10, Schannel — which .NET's HttpClient/SslStream use —
/// has no TLS 1.3 client, so the OS path can never complete the handshake (it fails with SEC_E_ILLEGAL_MESSAGE).
/// The reference app avoids the same limitation by bundling rustls; this is the .NET equivalent.
///
/// Callers must request the resource with the <c>http://</c> scheme (use <see cref="ToPlaintextScheme"/>) so that
/// <see cref="SocketsHttpHandler"/> does not wrap the already-encrypted stream a second time. The connect callback
/// upgrades the raw socket to TLS 1.3 itself and dials port 443 by default.
/// </remarks>
public static class ManagedTls
{
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Creates an HttpClient that tunnels every connection through a managed TLS 1.3 handshake.</summary>
    public static HttpClient CreateHttpClient()
    {
        // UseProxy = false is load-bearing: with the Windows system proxy enabled (mihomo mixed port),
        // SocketsHttpHandler would otherwise route the request to the proxy and hand ConnectCallback the
        // proxy endpoint — and the TLS handshake against a plain HTTP proxy port fails (alert 40).
        var handler = new SocketsHttpHandler { ConnectCallback = ConnectTlsAsync, UseProxy = false };
        return new HttpClient(handler, disposeHandler: true);
    }

    /// <summary>Swaps an <c>https://</c> URL to <c>http://</c> so HttpClient defers TLS to <see cref="ConnectTlsAsync"/>.</summary>
    public static string ToPlaintextScheme(string url) =>
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? string.Concat("http://", url.AsSpan("https://".Length))
            : url;

    private static async ValueTask<Stream> ConnectTlsAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port is 0 or 80 ? 443 : context.DnsEndPoint.Port;

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
            // The BouncyCastle handshake below reads/writes synchronously; bound it so a stalled peer can't hang forever.
            socket.ReceiveTimeout = (int)HandshakeTimeout.TotalMilliseconds;
            socket.SendTimeout = (int)HandshakeTimeout.TotalMilliseconds;

            var network = new NetworkStream(socket, ownsSocket: true);
            return await Task.Run(() =>
            {
                var protocol = new TlsClientProtocol(network);
                protocol.Connect(new ManagedTlsClient(host));
                return (Stream)protocol.Stream;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>A TLS 1.3/1.2 client that offers SNI and validates the server certificate against the OS trust store.</summary>
    private sealed class ManagedTlsClient : DefaultTlsClient
    {
        private readonly string _host;

        public ManagedTlsClient(string host)
            : base(new BcTlsCrypto(new SecureRandom())) => _host = host;

        protected override ProtocolVersion[] GetSupportedVersions() =>
            ProtocolVersion.TLSv13.DownTo(ProtocolVersion.TLSv12);

        protected override IList<ServerName> GetSniServerNames() =>
            new List<ServerName> { new ServerName(NameType.host_name, Encoding.ASCII.GetBytes(_host)) };

        public override TlsAuthentication GetAuthentication() => new HostAuthentication(_host);

        private sealed class HostAuthentication : TlsAuthentication
        {
            private readonly string _host;

            public HostAuthentication(string host) => _host = host;

            // Server does not request a client certificate.
            public TlsCredentials GetClientCredentials(Org.BouncyCastle.Tls.CertificateRequest certificateRequest) => null!;

            public void NotifyServerCertificate(TlsServerCertificate serverCertificate)
            {
                var presented = serverCertificate?.Certificate;
                if (presented is null || presented.IsEmpty)
                    throw new TlsFatalAlert(AlertDescription.certificate_unknown);

                var leaf = X509CertificateLoader.LoadCertificate(presented.GetCertificateAt(0).GetEncoded());
                using var chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                for (var i = 1; i < presented.Length; i++)
                    chain.ChainPolicy.ExtraStore.Add(X509CertificateLoader.LoadCertificate(presented.GetCertificateAt(i).GetEncoded()));

                if (!chain.Build(leaf))
                    throw new TlsFatalAlert(AlertDescription.bad_certificate);
                if (!leaf.MatchesHostname(_host))
                    throw new TlsFatalAlert(AlertDescription.bad_certificate);
            }
        }
    }
}
