using System.Net;
using System.Text;
using System.Text.Json;
using Explore.Application.Features.EventReporting.Models;
using Explore.Domain.Enums;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Services.Moderation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Tests.Infrastructure.Moderation;

public sealed class OspreyModerationSignalProviderTests
{
    [Test]
    public async Task EvaluateAsync_WhenProviderDisabled_ReturnsNonRetryableFailureWithoutHttpCall()
    {
        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var provider = CreateProvider(new OspreyProviderOptions(), handler);

        var result = await provider.EvaluateAsync(CreateEnvelope());

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Category).IsEqualTo("osprey_provider_disabled");
        await Assert.That(result.Error.IsTransient).IsFalse();
        await Assert.That(handler.Calls).IsEqualTo(0);
    }

    [Test]
    public async Task EvaluateAsync_WhenOspreyReturnsSignals_SendsSafePayloadAndMapsSignals()
    {
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var handler = new RecordingMessageHandler(_ => JsonResponse(HttpStatusCode.OK, $$"""
        {
          "signals": [
            {
              "signal_type": "policy_match",
              "policy_code": "event.spam",
              "score": 0.91,
              "verdict": "likely_violation",
              "recommended_action": "light_moderate",
              "safe_summary": "Likely spam promotion.",
              "external_signal_id": "signal-1",
              "correlation_id": "provider-correlation-1",
              "created_at_utc": "{{createdAt:O}}"
            }
          ]
        }
        """));
        var provider = CreateProvider(new OspreyProviderOptions
        {
            Enabled = true,
            EndpointUrl = "https://osprey.example/osprey",
            EvaluatePath = "/evaluate",
            ApiKey = "secret-token",
            EventType = "event_report"
        }, handler);

        var envelope = CreateEnvelope();
        var result = await provider.EvaluateAsync(envelope);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(handler.Calls).IsEqualTo(1);
        await Assert.That(handler.RequestUri!.ToString()).IsEqualTo("https://osprey.example/osprey/evaluate");
        await Assert.That(handler.AuthorizationScheme).IsEqualTo("Bearer");
        await Assert.That(handler.AuthorizationParameter).IsEqualTo("secret-token");
        await Assert.That(handler.RequestBody).Contains("\"reason_code\":\"spam\"");
        await Assert.That(handler.RequestBody).Contains("\"evidence_mode\":\"metadata_only\"");
        await Assert.That(handler.RequestBody).DoesNotContain("reporter_text");
        await Assert.That(handler.RequestBody).DoesNotContain("reporter_ip_hash");
        await Assert.That(result.Signals).Count().IsEqualTo(1);

        var signal = result.Signals.Single();
        await Assert.That(signal.TenantId).IsEqualTo(envelope.TenantId);
        await Assert.That(signal.ReportId).IsEqualTo(envelope.ReportId);
        await Assert.That(signal.EventId).IsEqualTo(envelope.EventId);
        await Assert.That(signal.Provider).IsEqualTo(EventReportSignalProvider.Osprey);
        await Assert.That(signal.SignalType).IsEqualTo("policy_match");
        await Assert.That(signal.PolicyCode).IsEqualTo("event.spam");
        await Assert.That(signal.Score).IsEqualTo(0.91m);
        await Assert.That(signal.Verdict).IsEqualTo(EventReportSignalVerdict.LikelyViolation);
        await Assert.That(signal.RecommendedAction).IsEqualTo(EventReportRecommendedAction.LightModerate);
        await Assert.That(signal.SafeSummary).IsEqualTo("Likely spam promotion.");
        await Assert.That(signal.ExternalSignalId).IsEqualTo("signal-1");
        await Assert.That(signal.CorrelationId).IsEqualTo("provider-correlation-1");
    }

    [Test]
    public async Task EvaluateAsync_WithTenantTargetOverride_UsesTenantEndpointAndMapsTarget()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse(HttpStatusCode.OK, """
        {
          "signals": [
            {
              "signal_type": "policy_match",
              "policy_code": "event.spam",
              "score": 0.88,
              "verdict": "needs_review",
              "recommended_action": "queue_review",
              "external_signal_id": "tenant-signal-1"
            }
          ]
        }
        """));
        var provider = CreateProvider(new OspreyProviderOptions
        {
            Enabled = false,
            EndpointUrl = "https://instance-osprey.example",
            EvaluatePath = "/evaluate",
            ApiKey = "instance-secret"
        }, handler);

        var envelope = CreateEnvelope() with
        {
            ProviderTargetScope = EventReportProviderTargetScope.Tenant,
            ProviderTargetId = "tenant-1",
            ProviderEndpointUrl = "https://tenant-osprey.example/root",
            ProviderApiKey = "tenant-secret"
        };

        var result = await provider.EvaluateAsync(envelope);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(handler.RequestUri!.ToString()).IsEqualTo("https://tenant-osprey.example/root/evaluate");
        await Assert.That(handler.AuthorizationParameter).IsEqualTo("tenant-secret");

        var signal = result.Signals.Single();
        await Assert.That(signal.ProviderTargetScope).IsEqualTo(EventReportProviderTargetScope.Tenant);
        await Assert.That(signal.ProviderTargetId).IsEqualTo("tenant-1");
    }

    [Test]
    public async Task EvaluateAsync_WhenOspreyReturnsServerError_ReturnsRetryableFailure()
    {
        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var provider = CreateProvider(new OspreyProviderOptions
        {
            Enabled = true,
            EndpointUrl = "https://osprey.example",
            EvaluatePath = "/api/v1/evaluate"
        }, handler);

        var result = await provider.EvaluateAsync(CreateEnvelope());

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Category).IsEqualTo("osprey_transient_http_failure");
        await Assert.That(result.Error.IsTransient).IsTrue();
    }

    [Test]
    public async Task EvaluateAsync_WhenOspreyReturnsInvalidJson_ReturnsNonRetryableInvalidResponse()
    {
        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ invalid", Encoding.UTF8, "application/json")
        });
        var provider = CreateProvider(new OspreyProviderOptions
        {
            Enabled = true,
            EndpointUrl = "https://osprey.example",
            EvaluatePath = "/api/v1/evaluate"
        }, handler);

        var result = await provider.EvaluateAsync(CreateEnvelope());

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Category).IsEqualTo("osprey_invalid_response");
        await Assert.That(result.Error.IsTransient).IsFalse();
    }

    private static OspreyModerationSignalProvider CreateProvider(
        OspreyProviderOptions options,
        HttpMessageHandler handler)
    {
        var client = new HttpClient(handler);
        return new OspreyModerationSignalProvider(
            new StaticHttpClientFactory(client),
            new StaticOptionsMonitor<OspreyProviderOptions>(options),
            NullLogger<OspreyModerationSignalProvider>.Instance);
    }

    private static EventReportProviderEnvelope CreateEnvelope() => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        "spam",
        "safety",
        "submitted",
        "open",
        "normal",
        DateTime.UtcNow,
        DateTime.UtcNow,
        "event-report-provider-sync:0197aae841a077b6983444b2616d9edc",
        "request-correlation-1",
        EventReportProviderEvidenceMode.MetadataOnly);

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StaticOptionsMonitor<TOptions>(TOptions currentValue) : IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue { get; } = currentValue;

        public TOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }

    private sealed class RecordingMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responseFactory(request);
        }
    }
}
