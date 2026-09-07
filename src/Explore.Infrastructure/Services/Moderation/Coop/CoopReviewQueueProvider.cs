using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.EventReporting.Models;
using Explore.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services.Moderation.Coop;

public sealed class CoopReviewQueueProvider(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<CoopProviderOptions> options,
    ILogger<CoopReviewQueueProvider> logger) : IReviewQueueProvider
{
    public const string HttpClientName = "CoopReviewQueueClient";

    private const int MaxItemTypeLength = 100;
    private const int MaxQueueCodeLength = 100;
    private const int MaxReasonCodeLength = 100;
    private const int MaxStatusCodeLength = 100;
    private const int MaxPriorityCodeLength = 100;
    private const int MaxProviderCaseIdLength = 200;
    private const int MaxProviderUrlLength = 500;
    private const int MaxCorrelationIdLength = 100;
    private const int MaxIdempotencyKeyLength = 200;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ReviewCaseSyncResult> MirrorCaseAsync(
        ReviewCaseEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var currentOptions = options.CurrentValue;
        string? endpointUrl = ResolveEndpointUrl(currentOptions, envelope);
        if (string.IsNullOrWhiteSpace(endpointUrl))
        {
            return ReviewCaseSyncResult.Disabled("Coop review queue provider is not enabled or configured.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(currentOptions.TimeoutSeconds, 1, 300)));

        try
        {
            using var request = CreateRequest(currentOptions, endpointUrl, envelope.ProviderApiKey, CreatePayload(envelope, currentOptions));
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var failure = MapHttpFailure(response.StatusCode);
                logger.LogWarning(
                    "Coop review queue mirror failed with status {StatusCode} and category {FailureCategory}",
                    (int)response.StatusCode,
                    failure.Category);
                return ReviewCaseSyncResult.Failure(
                    failure.Category,
                    failure.IsTransient,
                    failure.SafeDetail);
            }

            var responseBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            var providerResponse = string.IsNullOrWhiteSpace(responseBody)
                ? new CoopMirrorResponse()
                : JsonSerializer.Deserialize<CoopMirrorResponse>(responseBody, JsonOptions);

            if (providerResponse is null)
            {
                return ReviewCaseSyncResult.Failure(
                    "coop_invalid_response",
                    isTransient: false,
                    "Coop returned an empty or invalid response.");
            }

            return ReviewCaseSyncResult.Success(
                ResolveProviderCaseId(providerResponse, envelope),
                ResolveProviderUrl(providerResponse));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return ReviewCaseSyncResult.Failure(
                "coop_timeout",
                isTransient: true,
                "Coop review queue mirror timed out.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Coop review queue mirror request failed with {FailureType}", ex.GetType().Name);
            return ReviewCaseSyncResult.Failure(
                "coop_unreachable",
                isTransient: true,
                ex.GetType().Name);
        }
        catch (JsonException)
        {
            return ReviewCaseSyncResult.Failure(
                "coop_invalid_response",
                isTransient: false,
                "Coop returned a response that could not be parsed.");
        }
        catch (UriFormatException)
        {
            return ReviewCaseSyncResult.Failure(
                "coop_invalid_configuration",
                isTransient: false,
                "Coop endpoint configuration is invalid.");
        }
    }

    private static string? ResolveEndpointUrl(CoopProviderOptions currentOptions, ReviewCaseEnvelope envelope)
    {
        if (!string.IsNullOrWhiteSpace(envelope.ProviderEndpointUrl))
        {
            return envelope.ProviderEndpointUrl;
        }

        return currentOptions.Enabled ? currentOptions.EndpointUrl : null;
    }

    private static CoopMirrorRequest CreatePayload(
        ReviewCaseEnvelope envelope,
        CoopProviderOptions currentOptions) =>
        new()
        {
            ItemType = NormalizeRequired(currentOptions.ItemType, "event_report", MaxItemTypeLength),
            IdempotencyKey = NormalizeRequired(envelope.IdempotencyKey, "event-report-provider-sync", MaxIdempotencyKeyLength),
            CorrelationId = NormalizeOptional(envelope.CorrelationId, MaxCorrelationIdLength),
            ExpectedCaseConcurrencyStamp = envelope.CaseConcurrencyStamp,
            Item = new CoopItem
            {
                Id = envelope.ReportId.ToString("N"),
                TypeId = NormalizeRequired(currentOptions.ItemType, "event_report", MaxItemTypeLength),
                TenantId = envelope.TenantId,
                ReportId = envelope.ReportId,
                EventId = envelope.EventId,
                CaseId = envelope.CaseId
            },
            ReviewCase = new CoopReviewCase
            {
                QueueCode = NormalizeRequired(envelope.QueueCode, "default", MaxQueueCodeLength),
                CaseStatusCode = NormalizeRequired(envelope.CaseStatusCode, "open", MaxStatusCodeLength),
                PriorityCode = NormalizeRequired(envelope.PriorityCode, "normal", MaxPriorityCodeLength),
                ReasonCode = NormalizeRequired(envelope.ReasonCode, "unspecified", MaxReasonCodeLength),
                SubmittedAtUtc = envelope.SubmittedAtUtc,
                SlaDueAtUtc = envelope.SlaDueAtUtc
            },
            Evidence = new CoopEvidenceDescriptor
            {
                Mode = ToCode(envelope.EvidenceMode),
                ContentIncluded = false,
                SafeSummaryIncluded = false,
                ReporterTextIncluded = false
            }
        };

    private static HttpRequestMessage CreateRequest(
        CoopProviderOptions currentOptions,
        string endpointUrl,
        string? providerApiKey,
        CoopMirrorRequest payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, BuildMirrorUri(currentOptions, endpointUrl));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Idempotency-Key", payload.IdempotencyKey);

        string? apiKey = string.IsNullOrWhiteSpace(providerApiKey) ? currentOptions.ApiKey : providerApiKey;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var headerName = currentOptions.ApiKeyHeaderName.Trim();
            if (string.Equals(headerName, "Authorization", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
            }
            else
            {
                request.Headers.TryAddWithoutValidation(headerName, apiKey.Trim());
            }
        }

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    private static Uri BuildMirrorUri(CoopProviderOptions currentOptions, string endpointUrl)
    {
        var endpoint = new Uri(endpointUrl.Trim(), UriKind.Absolute);
        var endpointPath = endpoint.AbsolutePath.TrimEnd('/');
        var mirrorPath = currentOptions.MirrorPath.Trim();

        if (endpointPath.EndsWith(mirrorPath, StringComparison.OrdinalIgnoreCase))
        {
            return endpoint;
        }

        var builder = new UriBuilder(endpoint)
        {
            Path = string.IsNullOrEmpty(endpointPath)
                ? mirrorPath.TrimStart('/')
                : $"{endpointPath}/{mirrorPath.TrimStart('/')}",
            Query = string.Empty
        };

        return builder.Uri;
    }

    private static CoopFailure MapHttpFailure(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity =>
                new("coop_invalid_request", false, "Coop rejected the review queue mirror request."),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new("coop_auth_failed", false, "Coop rejected the configured credentials."),
            HttpStatusCode.Conflict =>
                new("coop_conflict", false, "Coop rejected the mirror request because the case already exists or conflicts."),
            HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests =>
                new("coop_transient_http_failure", true, $"HTTP {(int)statusCode}"),
            >= HttpStatusCode.InternalServerError =>
                new("coop_transient_http_failure", true, $"HTTP {(int)statusCode}"),
            _ => new("coop_http_failure", false, $"HTTP {(int)statusCode}")
        };
    }

    private static string ResolveProviderCaseId(
        CoopMirrorResponse response,
        ReviewCaseEnvelope envelope) =>
        NormalizeOptional(response.ProviderCaseId, MaxProviderCaseIdLength)
        ?? NormalizeOptional(response.CaseId, MaxProviderCaseIdLength)
        ?? NormalizeOptional(response.Id, MaxProviderCaseIdLength)
        ?? NormalizeOptional(response.Case?.Id, MaxProviderCaseIdLength)
        ?? $"coop-case:{envelope.CaseId:N}";

    private static string? ResolveProviderUrl(CoopMirrorResponse response) =>
        NormalizeOptional(response.ProviderUrl, MaxProviderUrlLength)
        ?? NormalizeOptional(response.CaseUrl, MaxProviderUrlLength)
        ?? NormalizeOptional(response.Url, MaxProviderUrlLength)
        ?? NormalizeOptional(response.Case?.Url, MaxProviderUrlLength);

    private static string NormalizeRequired(string? value, string fallback, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string ToCode<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var name = value.ToString();
        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var character = name[i];
            if (i > 0 && char.IsUpper(character))
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private sealed record CoopFailure(string Category, bool IsTransient, string SafeDetail);

    private sealed class CoopMirrorRequest
    {
        [JsonPropertyName("item_type")]
        public required string ItemType { get; init; }

        [JsonPropertyName("item")]
        public required CoopItem Item { get; init; }

        [JsonPropertyName("review_case")]
        public required CoopReviewCase ReviewCase { get; init; }

        [JsonPropertyName("evidence")]
        public required CoopEvidenceDescriptor Evidence { get; init; }

        [JsonPropertyName("idempotency_key")]
        public required string IdempotencyKey { get; init; }

        [JsonPropertyName("correlation_id")]
        public string? CorrelationId { get; init; }

        [JsonPropertyName("expected_case_concurrency_stamp")]
        public Guid ExpectedCaseConcurrencyStamp { get; init; }
    }

    private sealed class CoopItem
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("type_id")]
        public required string TypeId { get; init; }

        [JsonPropertyName("tenant_id")]
        public Guid TenantId { get; init; }

        [JsonPropertyName("report_id")]
        public Guid ReportId { get; init; }

        [JsonPropertyName("event_id")]
        public Guid EventId { get; init; }

        [JsonPropertyName("case_id")]
        public Guid CaseId { get; init; }
    }

    private sealed class CoopReviewCase
    {
        [JsonPropertyName("queue_code")]
        public required string QueueCode { get; init; }

        [JsonPropertyName("case_status")]
        public required string CaseStatusCode { get; init; }

        [JsonPropertyName("priority")]
        public required string PriorityCode { get; init; }

        [JsonPropertyName("reason_code")]
        public required string ReasonCode { get; init; }

        [JsonPropertyName("submitted_at_utc")]
        public DateTime SubmittedAtUtc { get; init; }

        [JsonPropertyName("sla_due_at_utc")]
        public DateTime? SlaDueAtUtc { get; init; }
    }

    private sealed class CoopEvidenceDescriptor
    {
        [JsonPropertyName("mode")]
        public required string Mode { get; init; }

        [JsonPropertyName("content_included")]
        public bool ContentIncluded { get; init; }

        [JsonPropertyName("safe_summary_included")]
        public bool SafeSummaryIncluded { get; init; }

        [JsonPropertyName("reporter_text_included")]
        public bool ReporterTextIncluded { get; init; }
    }

    private sealed class CoopMirrorResponse
    {
        [JsonPropertyName("provider_case_id")]
        public string? ProviderCaseId { get; init; }

        [JsonPropertyName("case_id")]
        public string? CaseId { get; init; }

        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("provider_url")]
        public string? ProviderUrl { get; init; }

        [JsonPropertyName("case_url")]
        public string? CaseUrl { get; init; }

        [JsonPropertyName("url")]
        public string? Url { get; init; }

        [JsonPropertyName("case")]
        public CoopCaseResponse? Case { get; init; }
    }

    private sealed class CoopCaseResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("url")]
        public string? Url { get; init; }
    }
}
