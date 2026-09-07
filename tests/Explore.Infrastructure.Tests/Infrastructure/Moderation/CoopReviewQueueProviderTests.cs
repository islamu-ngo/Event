using System.Net;
using System.Text;
using Explore.Application.Features.EventReporting.Models;
using Explore.Domain.Enums;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Services.Moderation.Coop;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Tests.Infrastructure.Moderation;

public sealed class CoopReviewQueueProviderTests
{
    [Test]
    public async Task MirrorCaseAsync_WhenProviderDisabled_ReturnsDisabledWithoutHttpCall()
    {
        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var provider = CreateProvider(new CoopProviderOptions(), handler);

        var result = await provider.MirrorCaseAsync(CreateEnvelope());

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ProviderDisabled).IsTrue();
        await Assert.That(result.Error!.Category).IsEqualTo("provider_disabled");
        await Assert.That(handler.Calls).IsEqualTo(0);
    }

    [Test]
    public async Task MirrorCaseAsync_WhenCoopReturnsCase_SendsSafePayloadAndMapsLink()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse(HttpStatusCode.Created, """
        {
          "provider_case_id": "coop-case-1",
          "provider_url": "https://coop.example/cases/coop-case-1"
        }
        """));
        var provider = CreateProvider(new CoopProviderOptions
        {
            Enabled = true,
            EndpointUrl = "https://coop.example/moderation",
            MirrorPath = "/api/v1/items",
            ApiKey = "secret-token",
            ItemType = "event_report"
        }, handler);

        var envelope = CreateEnvelope(EventReportProviderEvidenceMode.SafeSummaryOnly);
        var result = await provider.MirrorCaseAsync(envelope);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(handler.Calls).IsEqualTo(1);
        await Assert.That(handler.RequestUri!.ToString()).IsEqualTo("https://coop.example/moderation/api/v1/items");
        await Assert.That(handler.AuthorizationScheme).IsEqualTo("Bearer");
        await Assert.That(handler.AuthorizationParameter).IsEqualTo("secret-token");
        await Assert.That(handler.IdempotencyKey).IsEqualTo(envelope.IdempotencyKey);
        await Assert.That(handler.RequestBody).Contains("\"item_type\":\"event_report\"");
        await Assert.That(handler.RequestBody).Contains("\"queue_code\":\"safety\"");
        await Assert.That(handler.RequestBody).Contains("\"case_status\":\"open\"");
        await Assert.That(handler.RequestBody).Contains("\"priority\":\"normal\"");
        await Assert.That(handler.RequestBody).Contains("\"reason_code\":\"spam\"");
        await Assert.That(handler.RequestBody).Contains($"\"expected_case_concurrency_stamp\":\"{envelope.CaseConcurrencyStamp}\"");
        await Assert.That(handler.RequestBody).Contains("\"mode\":\"safe_summary_only\"");
        await Assert.That(handler.RequestBody).Contains("\"content_included\":false");
        await Assert.That(handler.RequestBody).DoesNotContain("reporter_text\":\"");
        await Assert.That(handler.RequestBody).DoesNotContain("reporter_ip_hash");
        await Assert.That(result.ProviderCaseId).IsEqualTo("coop-case-1");
        await Assert.That(result.ProviderUrl).IsEqualTo("https://coop.example/cases/coop-case-1");
    }

    [Test]
    public async Task MirrorCaseAsync_WithTenantTargetOverride_UsesTenantEndpointAndApiKey()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse(HttpStatusCode.Created, """
        {
          "provider_case_id": "tenant-coop-case-1",
          "provider_url": "https://tenant-coop.example/cases/tenant-coop-case-1"
        }
        """));
        var provider = CreateProvider(new CoopProviderOptions
        {
            Enabled = false,
            EndpointUrl = "https://instance-coop.example",
            MirrorPath = "/api/v1/items",
            ApiKey = "instance-secret"
        }, handler);

        var envelope = CreateEnvelope() with
        {
            ProviderTargetScope = EventReportProviderTargetScope.Tenant,
            ProviderTargetId = "tenant-1",
            ProviderEndpointUrl = "https://tenant-coop.example/root",
            ProviderApiKey = "tenant-secret"
        };

        var result = await provider.MirrorCaseAsync(envelope);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(handler.RequestUri!.ToString()).IsEqualTo("https://tenant-coop.example/root/api/v1/items");
        await Assert.That(handler.AuthorizationParameter).IsEqualTo("tenant-secret");
        await Assert.That(result.ProviderCaseId).IsEqualTo("tenant-coop-case-1");
    }

    [Test]
    public async Task MirrorCaseAsync_WhenCoopReturnsEmptySuccess_UsesLocalCaseFallbackForTracking()
    {
        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
        });
        var provider = CreateProvider(new CoopProviderOptions
        {
            Enabled = true,
            EndpointUrl = "https://coop.example",
            MirrorPath = "/api/v1/items"
        }, handler);
        var envelope = CreateEnvelope();

        var result = await provider.MirrorCaseAsync(envelope);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.ProviderCaseId).IsEqualTo($"coop-case:{envelope.CaseId:N}");
    }

    [Test]
    public async Task MirrorCaseAsync_WhenCoopReturnsServerError_ReturnsRetryableFailure()
    {
        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var provider = CreateProvider(new CoopProviderOptions
        {
            Enabled = true,
            EndpointUrl = "https://coop.example",
            MirrorPath = "/api/v1/items"
        }, handler);

        var result = await provider.MirrorCaseAsync(CreateEnvelope());

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Category).IsEqualTo("coop_transient_http_failure");
        await Assert.That(result.Error.IsTransient).IsTrue();
    }

    [Test]
    public async Task MirrorCaseAsync_WhenCoopReturnsInvalidJson_ReturnsNonRetryableInvalidResponse()
    {
        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ invalid", Encoding.UTF8, "application/json")
        });
        var provider = CreateProvider(new CoopProviderOptions
        {
            Enabled = true,
            EndpointUrl = "https://coop.example",
            MirrorPath = "/api/v1/items"
        }, handler);

        var result = await provider.MirrorCaseAsync(CreateEnvelope());

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Category).IsEqualTo("coop_invalid_response");
        await Assert.That(result.Error.IsTransient).IsFalse();
    }

    private static CoopReviewQueueProvider CreateProvider(
        CoopProviderOptions options,
        HttpMessageHandler handler)
    {
        var client = new HttpClient(handler);
        return new CoopReviewQueueProvider(
            new StaticHttpClientFactory(client),
            new StaticOptionsMonitor<CoopProviderOptions>(options),
            NullLogger<CoopReviewQueueProvider>.Instance);
    }

    private static ReviewCaseEnvelope CreateEnvelope(
        EventReportProviderEvidenceMode evidenceMode = EventReportProviderEvidenceMode.MetadataOnly) => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        EventReportExternalProvider.Coop,
        "safety",
        "open",
        "normal",
        "spam",
        DateTime.UtcNow,
        DateTime.UtcNow.AddHours(48),
        "event-report-provider-sync:0197aae841a077b6983444b2616d9edc",
        "request-correlation-1",
        evidenceMode);

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
        public string? IdempotencyKey { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            IdempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out var values)
                ? values.Single()
                : null;
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responseFactory(request);
        }
    }
}
