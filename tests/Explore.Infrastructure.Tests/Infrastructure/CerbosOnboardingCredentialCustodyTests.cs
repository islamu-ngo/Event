using System.Net;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

[NotInParallel]
public sealed class CerbosOnboardingCredentialCustodyTests
{
    [Test]
    public async Task CompletedPublicationCannotSupplyCredentialsToAnotherOperation()
    {
        await using var fixture = new Fixture();
        var credentials = FreshCredentials();
        var first = await fixture.Service.PublishAsync(oneTimeCredentials: credentials);
        var firstCount = fixture.Handler.RequestCount;
        var later = await fixture.Service.PublishAsync();
        var partial = await fixture.Service.PublishAsync(
            oneTimeCredentials: new PolicyPackageAdminCredentials(credentials.Username, string.Empty));
        var status = await fixture.Service.GetStatusAsync();

        await Assert.That(first.Succeeded).IsTrue();
        await Assert.That(firstCount).IsEqualTo(3);
        await Assert.That(later.Succeeded).IsFalse();
        await Assert.That(partial.Succeeded).IsFalse();
        await Assert.That(status.IsHealthy).IsFalse();
        await Assert.That(fixture.Handler.RequestCount).IsEqualTo(firstCount);
        await Assert.That(fixture.Handler.LastAuthorization).IsEqualTo(
            "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.Username}:{credentials.Password}")));
    }

    [Test]
    public async Task CancellationNeverQueuesCredentialsOrPublishesAnotherBatch()
    {
        await using var fixture = new Fixture();
        fixture.Handler.Block = true;
        using var cancellation = new CancellationTokenSource();
        var entered = fixture.Handler.Entered.Task;
        var call = fixture.Service.PublishAsync(cancellation.Token, FreshCredentials());
        await entered.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await call);
        var later = await fixture.Service.PublishAsync();
        await Assert.That(later.Succeeded).IsFalse();
        await Assert.That(fixture.Handler.RequestCount).IsEqualTo(1);
    }

    private static PolicyPackageAdminCredentials FreshCredentials() =>
        new(Guid.NewGuid().ToString("N"), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"cerbos-custody-{Guid.NewGuid():N}");
        private readonly HttpClient _client;
        public RecordingHandler Handler { get; } = new();
        public AuthenticatedPolicyPackageTestService Service { get; }

        public Fixture()
        {
            Directory.CreateDirectory(Path.Combine(_path, "_schemas"));
            File.WriteAllText(Path.Combine(_path, "islamuevent_event.yaml"),
                "apiVersion: api.cerbos.dev/v1\nresourcePolicy:\n  resource: islamuevent_event\n  version: default\n  rules: []\n");
            File.WriteAllText(Path.Combine(_path, "_schemas", "islamuevent_event.json"), "{\"type\":\"object\"}");
            _client = new HttpClient(Handler);
            var factory = Substitute.For<IHttpClientFactory>();
            factory.CreateClient(Arg.Any<string>()).Returns(_client);
            var resolver = Substitute.For<ICerbosConfigResolver>();
            resolver.ResolveAsync(Arg.Any<CancellationToken>()).Returns(new CerbosConfiguration
            {
                Endpoint = "https://pdp.example:3593",
                IsInstanceDefault = true
            });
            Service = new AuthenticatedPolicyPackageTestService(
                Options.Create(new CerbosPolicyPackageOptions { PoliciesPath = _path }),
                Options.Create(new CerbosAdminApiSettings { Endpoints = ["https://admin.example"] }),
                resolver, new CerbosAdminEndpointValidator(), factory,
                NullLogger<CerbosPolicyPackageService>.Instance,
                Options.Create(new CerbosSettings { GrpcEndpoint = "https://pdp.example:3593" }));
        }

        public ValueTask DisposeAsync()
        {
            _client.Dispose();
            Directory.Delete(_path, true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string? LastAuthorization { get; private set; }
        public bool Block { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastAuthorization = request.Headers.Authorization?.ToString();
            Entered.TrySetResult();
            if (Block)
                await _release.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }
}
