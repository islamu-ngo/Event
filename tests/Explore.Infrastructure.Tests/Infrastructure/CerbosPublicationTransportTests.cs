using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Explore.Infrastructure.Http;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Refit;

namespace Explore.Infrastructure.Tests.Infrastructure;

[NotInParallel]
public sealed class CerbosPublicationTransportTests
{
    [Test]
    public async Task PackagePublisherPreservesByoClassificationThroughRegisteredRefitTransport()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = $"https://byo.example:{((IPEndPoint)listener.LocalEndpoint).Port}";
        var path = Path.Combine(Path.GetTempPath(), $"cerbos-transport-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(path, "_schemas"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(path, "islamuevent_event.yaml"),
                "apiVersion: api.cerbos.dev/v1\nresourcePolicy:\n  resource: islamuevent_event\n  version: default\n  rules: []\n");
            await File.WriteAllTextAsync(Path.Combine(path, "_schemas", "islamuevent_event.json"), "{}");
            var services = new ServiceCollection();
            services.Configure<CerbosAdminApiSettings>(options => options.Endpoints = [endpoint]);
            var resolved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            CerbosAdminTransport.AddClients(services, (_, _) =>
            {
                resolved.TrySetResult();
                return Task.FromResult(new[] { IPAddress.Loopback });
            });
            await using var provider = services.BuildServiceProvider();
            var resolver = Substitute.For<ICerbosConfigResolver>();
            resolver.ResolveAsync(Arg.Any<CancellationToken>()).Returns(new CerbosConfiguration
            {
                Endpoint = "https://byo.example:3593",
                AdminEndpoint = endpoint,
                Mode = CerbosMode.CustomEndpoint,
                IsInstanceDefault = false
            });
            var publisher = new AuthenticatedPolicyPackageTestService(
                Options.Create(new CerbosPolicyPackageOptions { PoliciesPath = path }),
                provider.GetRequiredService<IOptions<CerbosAdminApiSettings>>(),
                resolver, provider.GetRequiredService<CerbosAdminEndpointValidator>(),
                provider.GetRequiredService<IHttpClientFactory>(),
                NullLogger<CerbosPolicyPackageService>.Instance,
                Options.Create(new CerbosSettings { GrpcEndpoint = "https://instance.example:3593" }));
            // This deadline includes real Local authentication and native custody setup.
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var result = await publisher.PublishAsync(deadline.Token,
                new PolicyPackageAdminCredentials(Guid.NewGuid().ToString("N"),
                    Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

            await Assert.That(result.Succeeded).IsFalse();
            await Assert.That(resolved.Task.IsCompletedSuccessfully).IsTrue();
            await Assert.That(listener.Pending()).IsFalse();
        }
        finally
        {
            Directory.Delete(path, true);
        }
    }

    [Test]
    public async Task ByoMixedDnsAnswerIsRejectedBeforeAnyConnection()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = new Uri($"https://byo.example:{((IPEndPoint)listener.LocalEndpoint).Port}");
        using var transport = CerbosAdminTransport.CreateHandler(true, [],
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("8.8.8.8"), IPAddress.Loopback }));
        using var client = new HttpClient(transport, disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.TryAddWithoutValidation("Authorization",
            "Basic " + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        request.Content = new StringContent("policy-canary");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            async () => { using var response = await client.SendAsync(request, deadline.Token); });

        await Assert.That(exception is not null &&
            OutboundEndpointDeniedException.Find(exception)?.FailureCategory == "private_network_blocked").IsTrue();
        await Assert.That(listener.Pending()).IsFalse();
    }

    [Test]
    [Arguments("169.254.169.254")]
    [Arguments("100.100.100.200")]
    [Arguments("fd00:ec2::254")]
    public async Task FixedOperatorOriginStillRejectsMetadata(string address)
    {
        var endpoint = new Uri("https://operator-pdp.example");
        using var transport = CerbosAdminTransport.CreateHandler(false, [endpoint],
            (_, _) => Task.FromResult(new[] { IPAddress.Parse(address) }));
        using var client = new HttpClient(transport, disposeHandler: false);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            async () => { using var response = await client.GetAsync(endpoint, deadline.Token); });
        await Assert.That(exception is not null &&
            OutboundEndpointDeniedException.Find(exception)?.FailureCategory == "cloud_metadata_blocked").IsTrue();
    }

    [Test]
    [Arguments(301)]
    [Arguments(302)]
    [Arguments(303)]
    [Arguments(307)]
    [Arguments(308)]
    public async Task RefitFixedOriginAllowsLocalOperatorButNeverFollowsRedirect(int status)
    {
        using var origin = new TcpListener(IPAddress.Loopback, 0);
        using var destination = new TcpListener(IPAddress.Loopback, 0);
        origin.Start();
        destination.Start();
        var endpoint = new Uri($"http://operator-pdp.example:{((IPEndPoint)origin.LocalEndpoint).Port}");
        var redirect = $"http://127.0.0.1:{((IPEndPoint)destination.LocalEndpoint).Port}/stolen";
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var accepted = origin.AcceptTcpClientAsync(deadline.Token);
        using var transport = CerbosAdminTransport.CreateHandler(false, [endpoint],
            (_, _) => Task.FromResult(new[] { IPAddress.Loopback }));
        using var client = new HttpClient(transport, disposeHandler: false)
        { BaseAddress = endpoint };
        var api = RestService.For<ICerbosAdminApi>(client, new RefitSettings
        {
            ExceptionFactory = _ => ValueTask.FromResult<Exception?>(null)
        });
        var credential = "Basic " + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var call = api.ReloadInstanceAsync(credential, cancellationToken: deadline.Token);
        using var peer = await accepted;
        await using var stream = peer.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var headers = new List<string>();
        while (await reader.ReadLineAsync(deadline.Token) is { Length: > 0 } line)
            headers.Add(line);
        var responseBytes = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} Redirect\r\nLocation: {redirect}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(responseBytes, deadline.Token);
        using var response = await call;

        await Assert.That(response.StatusCode).IsEqualTo((HttpStatusCode)status);
        await Assert.That(headers.Any(header => header == $"Authorization: {credential}")).IsTrue();
        await Assert.That(destination.Pending()).IsFalse();
    }

    [Test]
    public async Task InstanceClientRejectsAnotherOriginBeforeDns()
    {
        var resolved = false;
        using var transport = CerbosAdminTransport.CreateHandler(false,
            [new Uri("https://operator.example")], (_, _) =>
            {
                resolved = true;
                return Task.FromResult(new[] { IPAddress.Loopback });
            });
        using var client = new HttpClient(transport, disposeHandler: false);
        var exception = await Assert.ThrowsAsync<OutboundEndpointDeniedException>(
            async () => { using var response = await client.GetAsync("https://other.example/admin/policy"); });
        await Assert.That(exception?.FailureCategory).IsEqualTo("origin_mismatch");
        await Assert.That(resolved).IsFalse();
    }
}
