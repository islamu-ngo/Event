using System.Net;
using System.Text.Json;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Models;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

[NotInParallel]
public sealed class CerbosPublicationDiagnosticTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task NativePublicationKeepsFailureIssueWithoutPrivateTransportTypeOrContent(int operation)
    {
        string canary = Guid.CreateVersion7().ToString("N");
        string directory = Path.Combine(Path.GetTempPath(), "cerbos-diagnostic-" + canary);
        Directory.CreateDirectory(Path.Combine(directory, "_schemas"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "islamuevent_diagnostic.yaml"),
                "apiVersion: api.cerbos.dev/v1\nresourcePolicy:\n  resource: islamuevent_diagnostic\n  version: default\n  rules: []\n");
            await File.WriteAllTextAsync(Path.Combine(directory, "_schemas", "islamuevent_diagnostic.json"), "{}");
            const string endpoint = "https://operator-pdp.example:3593";
            var configuration = Substitute.For<ICerbosConfigResolver>();
            configuration.ResolveAsync(Arg.Any<CancellationToken>()).Returns(new CerbosConfiguration
            {
                Endpoint = "https://operator-pdp.example:3592",
                AdminEndpoint = endpoint,
                Mode = CerbosMode.CustomEndpoint,
                AdminUsername = canary,
                AdminPassword = canary,
                IsInstanceDefault = false
            });
            var settings = Options.Create(new CerbosAdminApiSettings { Endpoints = [endpoint] });
            string failedPath = operation switch
            {
                0 => "/admin/schema",
                1 => "/admin/store/reload",
                2 => "/admin/policies",
                3 => "/admin/policy",
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
            var transport = new PrivateTransport(canary, failedPath);
            using var client = new HttpClient(transport);
            var logger = new Collector();
            var packageOptions = Options.Create(new CerbosPolicyPackageOptions { PoliciesPath = directory });
            var publisher = new CerbosPolicyPackageService(
                packageOptions, settings, configuration, Substitute.For<ISecretResolver>(),
                new CerbosAdminEndpointValidator(), new ClientFactory(client), logger,
                Substitute.For<IEventResourcePolicyPublicationFence>(),
                Options.Create(new CerbosSettings { GrpcEndpoint = "https://instance.example:3593" }));
            // Bound the native HTTP failure path; no external provider or vault is contacted.
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));

            var credentials = new PolicyPackageAdminCredentials(canary, canary);
            string resultJson;
            if (operation < 2)
            {
                var result = await publisher.PublishAsync(deadline.Token, credentials);
                await Assert.That(result.Succeeded).IsFalse();
                await Assert.That(result.IssueCode).IsEqualTo(operation == 1
                    ? PolicyPackageIssueCode.ReloadFailed : PolicyPackageIssueCode.AdminApiUnavailable);
                resultJson = JsonSerializer.Serialize(result);
            }
            else
            {
                var result = await publisher.GetStatusAsync(deadline.Token);
                await Assert.That(result.IssueCode).IsEqualTo(operation == 2
                    ? PolicyPackageIssueCode.PackageStatusUnknown : PolicyPackageIssueCode.None);
                await Assert.That(result.ObservedRevision).IsNull();
                await Assert.That(result.Warnings.Count).IsGreaterThan(0);
                resultJson = JsonSerializer.Serialize(result);
            }
            await Assert.That(transport.FailedPath).IsEqualTo(failedPath);
            await Assert.That(logger.Values.Any(row =>
                row.State.Any(pair => pair.Key is "TransportFailure" or "FailureType" && Equals(pair.Value, "unavailable")))).IsTrue();
            string output = JsonSerializer.Serialize(logger.Values) + resultJson;
            await Assert.That(output).DoesNotContain(canary);
            await Assert.That(output).DoesNotContain(nameof(PrivateTransportException));
            await Assert.That(output).DoesNotContain("forged-transport-event");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class PrivateTransportException(string message) : IOException(message);

    private sealed class PrivateTransport(string canary, string failedPath) : HttpMessageHandler
    {
        public string? FailedPath { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == failedPath)
            {
                FailedPath = path;
                return Task.FromException<HttpResponseMessage>(new PrivateTransportException(canary + "\r\nforged-transport-event"));
            }
            string body = path == "/admin/policies"
                ? "{\"policyIds\":[\"resource.islamuevent_diagnostic.vdefault\"]}" : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed record Export(string Message, KeyValuePair<string, object?>[] State, string? Exception);
    private sealed class Collector : ILogger<CerbosPolicyPackageService>
    {
        public List<Export> Values { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Values.Add(new(
                formatter(state, exception),
                state is IEnumerable<KeyValuePair<string, object?>> properties ? properties.ToArray() : [],
                exception?.ToString()));
    }
}
