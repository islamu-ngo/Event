using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Channels;
using Explore.Infrastructure.Http;
using Explore.Infrastructure.Webhooks;
using Explore.Infrastructure.Services.Registration.Providers.SubmissionSinks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions;
using TUnit.Core;

namespace Explore.Infrastructure.Tests.Infrastructure.Http;

public partial class OutboundTransportSecurityTests
{
    [Test]
    [Arguments(WebhookDeliveryDrainService.HttpClientName)]
    [Arguments(WebhookRegistrationProviderSubmissionSink.HttpClientName)]
    public async Task RegisteredWebhookClientDeniesPrivatePeerBeforeBodyAndSignature(string clientName)
    {
        await using var server = new OutboundTlsPeer("outbound.example");
        var services = new ServiceCollection();
        services.ConfigureInfrastructureServices(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);
        using var content = new OutboundTlsPayload();
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri($"https://127.0.0.1:{server.Port}/delivery"))
        { Content = content };
        request.Headers.Add("Signature", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Exception? failure = null;
        try
        {
            using var response = await client.SendAsync(request, server.Deadline);
        }
        catch (HttpRequestException exception)
        {
            failure = exception;
        }
        catch (OutboundEndpointDeniedException exception)
        {
            failure = exception;
        }
        client.Dispose();
        await server.DrainAsync();
        await Assert.That(failure is not null
            ? OutboundEndpointDeniedException.Find(failure)?.FailureCategory : null)
            .IsEqualTo("private_network_blocked");
        await Assert.That(content.SerializationCount).IsEqualTo(0);
        await Assert.That(server.RequestCount).IsEqualTo(0);
        await Assert.That(server.SignatureCount).IsEqualTo(0);
        await Assert.That(server.BodyBytes).IsEqualTo(0L);
    }

    [Test]
    [Arguments("::ffff:169.254.169.254")]
    [Arguments("10.0.0.1")]
    public async Task MixedConnectionTimeAnswersRejectBeforeAnySocketHandoff(string deniedAddress)
    {
        await using var server = new OutboundTlsPeer("outbound.example");
        var policy = new OutboundEndpointPolicy(allowedPrivateCidrs: ["127.0.0.1/32"]);
        // An earlier permitted resolution does not authorize the connection-time answer set.
        policy.RequireAllowed(server.Endpoint, [IPAddress.Loopback]);
        var connector = new VettedAddressConnector(() => policy,
            (_, _) => Task.FromResult(new[] { IPAddress.Loopback, IPAddress.Parse(deniedAddress) }));
        using var handler = server.CreateTrustedHandler(connector);
        using var client = new HttpClient(handler);
        using var content = new OutboundTlsPayload();
        using var request = new HttpRequestMessage(HttpMethod.Post, server.Endpoint) { Content = content };
        request.Headers.Add("Signature", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Exception? failure = null;
        try
        {
            using var response = await client.SendAsync(request, server.Deadline);
        }
        catch (HttpRequestException exception)
        {
            failure = exception;
        }
        catch (OutboundEndpointDeniedException exception)
        {
            failure = exception;
        }
        client.Dispose();
        await server.DrainAsync();
        await Assert.That(failure is not null
            ? OutboundEndpointDeniedException.Find(failure) is not null : false).IsTrue();
        await Assert.That(content.SerializationCount).IsEqualTo(0);
        await Assert.That(server.RequestCount).IsEqualTo(0);
        await Assert.That(server.BodyBytes).IsEqualTo(0L);
        await Assert.That(server.SignatureCount).IsEqualTo(0);
    }

    [Test]
    public async Task VettedTlsConnection_RetainsOriginalHostnameForSniAndHttpHost()
    {
        await using var server = new OutboundTlsPeer("outbound.example");
        string? resolvedHost = null;
        var connector = new VettedAddressConnector(
            () => new OutboundEndpointPolicy(allowedPrivateCidrs: ["127.0.0.1/32"]),
            (host, _) =>
            {
                resolvedHost = host;
                return Task.FromResult(new[] { IPAddress.Loopback });
            });
        using var handler = server.CreateTrustedHandler(connector);
        using var client = new HttpClient(handler);
        var received = server.NextRequestAsync();
        using var response = await client.GetAsync(server.Endpoint, server.Deadline);
        var request = await received;

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(resolvedHost).IsEqualTo("outbound.example");
        await Assert.That(request.Sni).IsEqualTo("outbound.example");
        await Assert.That(request.Headers).Contains($"Host: outbound.example:{server.Port}\r\n");
    }

    [Test]
    public async Task VettedTlsConnection_RejectsTrustedCertificateForWrongHostname()
    {
        await using var server = new OutboundTlsPeer("different.example");
        var connector = new VettedAddressConnector(
            () => new OutboundEndpointPolicy(allowedPrivateCidrs: ["127.0.0.1/32"]),
            (_, _) => Task.FromResult(new[] { IPAddress.Loopback }));
        using var handler = server.CreateTrustedHandler(connector);
        using var client = new HttpClient(handler);
        using var content = new OutboundTlsPayload();
        var handshake = server.NextHandshakeAsync();
        Exception? failure = null;
        try
        {
            using var response = await client.PostAsync(server.Endpoint, content, server.Deadline);
        }
        catch (HttpRequestException exception)
        {
            failure = exception;
        }

        await handshake;
        client.Dispose();
        await server.DrainAsync();
        await Assert.That(FindTlsFailure<AuthenticationException>(failure) is not null).IsTrue();
        await Assert.That(content.SerializationCount).IsEqualTo(0);
        await Assert.That(server.RequestCount).IsEqualTo(0);
        await Assert.That(server.BodyBytes).IsEqualTo(0L);
        await Assert.That(server.SignatureCount).IsEqualTo(0);
    }

    [Test]
    public async Task RepeatedTlsRequest_RechecksLatestPolicyBeforeSendingBodyOrSignature()
    {
        await AssertRepeatedTlsRequestDeniedAsync(changeResolution: false);
    }

    [Test]
    public async Task RepeatedTlsRequest_RechecksLatestDnsAnswerBeforeSendingBodyOrSignature()
    {
        await AssertRepeatedTlsRequestDeniedAsync(changeResolution: true);
    }

    private static async Task AssertRepeatedTlsRequestDeniedAsync(bool changeResolution)
    {
        await using var server = new OutboundTlsPeer("outbound.example");
        var policy = new OutboundEndpointPolicy(allowedPrivateCidrs: ["127.0.0.1/32"]);
        IPAddress[] addresses = [IPAddress.Loopback];
        var latestResolution = new TaskCompletionSource<IPAddress[]>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var observeLatestResolution = false;
        var connector = new VettedAddressConnector(
            () => policy,
            (_, _) =>
            {
                if (observeLatestResolution)
                {
                    latestResolution.TrySetResult(addresses);
                }

                return Task.FromResult(addresses);
            });
        using var handler = server.CreateTrustedHandler(connector);
        using var client = new HttpClient(handler);
        var firstReceived = server.NextRequestAsync();
        using (var response = await client.GetAsync(server.Endpoint, server.Deadline))
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }

        await firstReceived;
        if (changeResolution)
        {
            // The initial exact CIDR must not authorize a later loopback DNS answer.
            addresses = [IPAddress.Parse("127.0.0.2")];
        }
        else
        {
            policy = new OutboundEndpointPolicy();
        }

        observeLatestResolution = true;
        using var content = new OutboundTlsPayload();
        using var request = new HttpRequestMessage(HttpMethod.Post, server.Endpoint)
        {
            Content = content
        };
        request.Headers.Add("Signature", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var resolutionObserved = latestResolution.Task.WaitAsync(server.Deadline);
        Exception? failure = null;
        try
        {
            using var response = await client.SendAsync(request, server.Deadline);
        }
        catch (HttpRequestException exception)
        {
            failure = exception;
        }
        catch (OutboundEndpointDeniedException exception)
        {
            failure = exception;
        }

        await resolutionObserved;
        client.Dispose();
        await server.DrainAsync();
        await Assert.That(FindTlsFailure<OutboundEndpointDeniedException>(failure) is not null).IsTrue();
        await Assert.That(content.SerializationCount).IsEqualTo(0);
        await Assert.That(server.RequestCount).IsEqualTo(1);
        await Assert.That(server.BodyBytes).IsEqualTo(0L);
        await Assert.That(server.SignatureCount).IsEqualTo(0);
    }

    private static TException? FindTlsFailure<TException>(Exception? exception)
        where TException : Exception
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is TException matching)
            {
                return matching;
            }
        }

        return null;
    }

    private sealed class OutboundTlsPayload : HttpContent
    {
        private readonly byte[] _body = RandomNumberGenerator.GetBytes(64);
        private int _serializationCount;

        public int SerializationCount => Volatile.Read(ref _serializationCount);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            Interlocked.Increment(ref _serializationCount);
            await stream.WriteAsync(_body);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _body.Length;
            return true;
        }
    }

    private sealed record OutboundTlsRequest(string Sni, string Headers);

    /// <summary>
    /// Keeps successful HTTP/1.1 connections open so repeat-request tests can detect
    /// reuse that bypasses the connector. Disposal drains accepted connections after
    /// the client closes, rather than inferring absence from a timed observation.
    /// </summary>
    private sealed class OutboundTlsPeer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _deadline = new(TimeSpan.FromSeconds(15));
        private readonly CancellationTokenSource _acceptCancellation = new();
        private readonly RSA _rootKey = RSA.Create(2048);
        private readonly RSA _leafKey = RSA.Create(2048);
        private readonly X509Certificate2 _root;
        private readonly X509Certificate2 _leaf;
        private readonly Channel<OutboundTlsRequest> _requests = Channel.CreateUnbounded<OutboundTlsRequest>();
        private readonly Channel<string> _handshakes = Channel.CreateUnbounded<string>();
        private readonly List<Task> _connections = [];
        private readonly Task _acceptLoop;
        private int _requestCount;
        private long _bodyBytes;
        private int _signatureCount;
        private bool _drained;

        public OutboundTlsPeer(string certificateHost)
        {
            var now = DateTimeOffset.UtcNow;
            var rootRequest = new CertificateRequest(
                "CN=Outbound transport ephemeral root", _rootKey,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign, true));
            _root = rootRequest.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(1));

            var leafRequest = new CertificateRequest(
                $"CN={certificateHost}", _leafKey,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName(certificateHost);
            leafRequest.CertificateExtensions.Add(names.Build());
            leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            leafRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
            using var issued = leafRequest.Create(
                _root, now.AddMinutes(-1), now.AddHours(1), RandomNumberGenerator.GetBytes(16));
            _leaf = issued.CopyWithPrivateKey(_leafKey);

            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Endpoint = new Uri($"https://outbound.example:{Port}/delivery");
            _acceptLoop = AcceptAsync();
        }

        public int Port { get; }
        public Uri Endpoint { get; }
        public CancellationToken Deadline => _deadline.Token;
        public int RequestCount => Volatile.Read(ref _requestCount);
        public long BodyBytes => Interlocked.Read(ref _bodyBytes);
        public int SignatureCount => Volatile.Read(ref _signatureCount);

        public Task<OutboundTlsRequest> NextRequestAsync() => _requests.Reader.ReadAsync(Deadline).AsTask();
        public Task<string> NextHandshakeAsync() => _handshakes.Reader.ReadAsync(Deadline).AsTask();

        public SocketsHttpHandler CreateTrustedHandler(VettedAddressConnector connector)
        {
            var handler = connector.CreateHandler(TimeSpan.FromSeconds(5));
            // Custom chain trust changes only the authority: SslStream's default
            // hostname validation remains enabled, including for the wrong-host case.
            handler.SslOptions.CertificateChainPolicy = new X509ChainPolicy
            {
                TrustMode = X509ChainTrustMode.CustomRootTrust,
                RevocationMode = X509RevocationMode.NoCheck
            };
            handler.SslOptions.CertificateChainPolicy.CustomTrustStore.Add(_root);
            handler.SslOptions.CertificateChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
            return handler;
        }

        private async Task AcceptAsync()
        {
            try
            {
                while (true)
                {
                    var socket = await _listener.AcceptTcpClientAsync(_acceptCancellation.Token);
                    _connections.Add(ServeAsync(socket));
                }
            }
            catch (OperationCanceledException) when (_acceptCancellation.IsCancellationRequested)
            {
            }
        }

        private async Task ServeAsync(TcpClient socket)
        {
            using (socket)
            await using (var tls = new SslStream(socket.GetStream()))
            {
                var sni = string.Empty;
                try
                {
                    await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        ServerCertificateSelectionCallback = (_, hostname) =>
                        {
                            sni = hostname ?? string.Empty;
                            _handshakes.Writer.TryWrite(sni);
                            return _leaf;
                        }
                    }, Deadline);
                }
                catch (AuthenticationException)
                {
                    return;
                }
                catch (IOException)
                {
                    // A client rejecting the certificate can close during the handshake.
                    return;
                }

                var singleByte = new byte[1];
                while (true)
                {
                    var headers = new StringBuilder();
                    while (!headers.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                    {
                        var count = await tls.ReadAsync(singleByte, Deadline);
                        if (count == 0)
                        {
                            if (headers.Length != 0)
                            {
                                throw new IOException("Client closed in the middle of HTTP headers.");
                            }

                            return;
                        }

                        headers.Append((char)singleByte[0]);
                        if (headers.Length > 16384)
                        {
                            throw new IOException("HTTP headers exceeded the test peer limit.");
                        }
                    }

                    var headerText = headers.ToString();
                    Interlocked.Increment(ref _requestCount);
                    var contentLength = 0;
                    foreach (var line in headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (line.StartsWith("Signature:", StringComparison.OrdinalIgnoreCase))
                        {
                            Interlocked.Increment(ref _signatureCount);
                        }

                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        {
                            contentLength = int.Parse(line["Content-Length:".Length..].Trim(),
                                System.Globalization.CultureInfo.InvariantCulture);
                        }
                    }

                    for (var index = 0; index < contentLength; index++)
                    {
                        if (await tls.ReadAsync(singleByte, Deadline) == 0)
                        {
                            throw new IOException("Client closed in the middle of the HTTP body.");
                        }

                        Interlocked.Increment(ref _bodyBytes);
                    }

                    _requests.Writer.TryWrite(new OutboundTlsRequest(sni, headerText));
                    await tls.WriteAsync(
                        "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: keep-alive\r\n\r\n"u8.ToArray(),
                        Deadline);
                    await tls.FlushAsync(Deadline);
                }
            }
        }

        public async Task DrainAsync()
        {
            if (_drained)
            {
                return;
            }

            await _acceptCancellation.CancelAsync();
            await _acceptLoop.WaitAsync(Deadline);
            _listener.Stop();
            await Task.WhenAll(_connections).WaitAsync(Deadline);
            _drained = true;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await DrainAsync();
            }
            finally
            {
                _listener.Stop();
                _acceptCancellation.Dispose();
                _deadline.Dispose();
                _leaf.Dispose();
                _root.Dispose();
                _leafKey.Dispose();
                _rootKey.Dispose();
            }
        }
    }
}
