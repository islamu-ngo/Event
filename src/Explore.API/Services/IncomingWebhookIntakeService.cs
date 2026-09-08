using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Domain;
using Microsoft.Extensions.Primitives;

namespace Explore.API.Services;

public sealed class IncomingWebhookIntakeService(
    IIncomingWebhookVerifierRegistry verifierRegistry,
    IIncomingWebhookMessageRepository incomingWebhookMessageRepository,
    IWebhookRetentionPolicyResolver retentionPolicyResolver,
    TimeProvider timeProvider,
    ILogger<IncomingWebhookIntakeService> logger) : IIncomingWebhookIntakeService
{
    private const int BufferThresholdBytes = 30 * 1024;
    private const int MaxProviderLength = IncomingWebhookMessage.MaxProviderLength;
    private const int MaxProviderMessageIdLength = IncomingWebhookMessage.MaxProviderMessageIdLength;
    private const int MaxIdempotencyKeyLength = IncomingWebhookMessage.MaxIdempotencyKeyLength;
    private const int MaxEventTypeLength = IncomingWebhookMessage.MaxEventTypeLength;
    private const int MaxSafeHeaderValueLength = 4096;
    public const string VerificationReceiptHeader = "X-Registration-Verification-Receipt";

    private static readonly string[] SensitiveHeaderFragments =
    [
        "authorization",
        "cookie",
        "signature",
        "secret",
        "token",
        "api-key",
        "apikey",
        "key"
    ];

    public async Task<IncomingWebhookReadResult> ReadAndVerifyAsync(
        HttpRequest request,
        string provider,
        long maxBodyBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedProvider = NormalizeRequired(provider, MaxProviderLength);
        var headers = CaptureHeaders(request.Headers);

        byte[] bodyBytes;
        try
        {
            bodyBytes = await ReadBodyBytesAsync(request, maxBodyBytes, cancellationToken);
        }
        catch (IOException)
        {
            return IncomingWebhookReadResult.Failure(
                normalizedProvider,
                StatusCodes.Status413PayloadTooLarge,
                "Incoming webhook body is too large",
                "The incoming webhook body exceeds the configured size limit.",
                $"{normalizedProvider}_webhook_body_too_large");
        }

        if (bodyBytes.Length == 0)
        {
            return IncomingWebhookReadResult.Failure(
                normalizedProvider,
                StatusCodes.Status400BadRequest,
                "Empty incoming webhook body",
                "The incoming webhook body is required.",
                $"{normalizedProvider}_webhook_body_empty");
        }

        string rawPayload;
        try
        {
            rawPayload = new UTF8Encoding(false, true).GetString(bodyBytes);
        }
        catch (DecoderFallbackException)
        {
            return IncomingWebhookReadResult.Failure(
                normalizedProvider,
                StatusCodes.Status400BadRequest,
                "Incoming webhook encoding is invalid",
                "The incoming webhook body must be valid UTF-8.",
                $"{normalizedProvider}_webhook_encoding_invalid");
        }
        var payloadHash = ComputePayloadHash(bodyBytes);
        var receivedAt = DateTimeOffset.UtcNow;
        IncomingWebhookVerificationResult verification;
        try
        {
            var verifier = verifierRegistry.GetRequired(normalizedProvider);
            verification = await verifier.VerifyAsync(
                new IncomingWebhookContext(normalizedProvider, rawPayload, bodyBytes, headers, receivedAt),
                cancellationToken);
        }
        catch (JsonException)
        {
            verification = IncomingWebhookVerificationResult.Rejected(
                $"{normalizedProvider}_webhook_format_invalid",
                "The incoming webhook could not be verified.");
        }

        if (!verification.IsVerified)
        {
            var failureCategory = NormalizeFailureCategory(verification.FailureCategory, normalizedProvider);
            var statusCode = MapVerificationStatusCode(failureCategory);
            return IncomingWebhookReadResult.Failure(
                normalizedProvider,
                statusCode,
                "Incoming webhook verification failed",
                verification.SafeDetail ?? "The incoming webhook could not be verified.",
                failureCategory);
        }

        return IncomingWebhookReadResult.Success(
            normalizedProvider,
            rawPayload,
            bodyBytes,
            receivedAt,
            payloadHash,
            string.IsNullOrWhiteSpace(request.ContentType) ? "application/octet-stream" : request.ContentType,
            "utf-8",
            headers,
            verification);
    }

    public async Task<IncomingWebhookCaptureResult> CaptureAsync(
        IncomingWebhookReadResult readResult,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(readResult);

        if (!readResult.Succeeded ||
            readResult.Verification is null ||
            readResult.RawPayloadBytes.IsEmpty ||
            string.IsNullOrWhiteSpace(readResult.PayloadHash))
        {
            return IncomingWebhookCaptureResult.Failure(
                StatusCodes.Status400BadRequest,
                "Incoming webhook was not verified",
                "Only verified incoming webhooks can be captured.",
                $"{readResult.Provider}_webhook_not_verified");
        }

        var tenantId = readResult.Verification.TenantId;
        if (tenantId is null || tenantId == Guid.Empty)
        {
            return IncomingWebhookCaptureResult.Failure(
                StatusCodes.Status400BadRequest,
                "Incoming webhook tenant is required",
                "The incoming webhook could not be associated with a tenant.",
                $"{readResult.Provider}_webhook_tenant_required");
        }

        var normalizedProvider = NormalizeRequired(readResult.Provider, MaxProviderLength);
        var resolvedProviderMessageId = FirstNonBlank(
            readResult.Verification.ProviderMessageId,
            readResult.PayloadHash);
        var resolvedIdempotencyKey = FirstNonBlank(
            readResult.Verification.IdempotencyKey,
            resolvedProviderMessageId);
        var resolvedEventType = FirstNonBlank(readResult.Verification.EventType);
        if (resolvedProviderMessageId is null || resolvedProviderMessageId.Length > MaxProviderMessageIdLength ||
            resolvedIdempotencyKey is null || resolvedIdempotencyKey.Length > MaxIdempotencyKeyLength ||
            resolvedEventType?.Length > MaxEventTypeLength)
        {
            return IncomingWebhookCaptureResult.Failure(
                StatusCodes.Status400BadRequest,
                "Incoming webhook identity is invalid",
                "Provider message identity, idempotency identity, or event type exceeds its allowed size.",
                "incoming_webhook_identity_invalid");
        }

        var normalizedProviderMessageId = resolvedProviderMessageId;
        var normalizedIdempotencyKey = resolvedIdempotencyKey;

        var nowOffset = timeProvider.GetUtcNow();
        var now = nowOffset.UtcDateTime;
        var retention = retentionPolicyResolver.Resolve(
            readResult.ReceivedAt,
            nowOffset);
        ReadOnlyMemory<byte> retainedPayload = readResult.Verification.RetainedPayloadBytes.IsEmpty
            ? readResult.RawPayloadBytes
            : readResult.Verification.RetainedPayloadBytes;
        WebhookPayloadProvenance payloadProvenance = readResult.Verification.RetainedPayloadBytes.IsEmpty
            ? WebhookPayloadProvenance.ExactBytes
            : WebhookPayloadProvenance.NormalizedProviderEnvelope;
        var message = IncomingWebhookMessage.CreateVerified(
            tenantId.Value,
            normalizedProvider,
            normalizedProviderMessageId,
            normalizedIdempotencyKey,
            resolvedEventType,
            retainedPayload.Span,
            readResult.PayloadHash,
            readResult.ContentType,
            readResult.ContentEncoding,
            SerializeSafeHeaders(readResult.Headers, readResult.Verification.Receipt),
            readResult.ReceivedAt.UtcDateTime,
            now,
            retention.InboundPayloadRetentionUntil.UtcDateTime,
            retention.PolicyVersion,
            retention.ProcessingAttemptRetentionUntil.UtcDateTime,
            retention.DeadLetterEvidenceRetentionUntil.UtcDateTime,
            retention.ReplayWindowUntil.UtcDateTime,
            retention.OperationalLogRetentionUntil.UtcDateTime,
            readResult.Verification.WebhookConsumerProviderBindingId,
            payloadProvenance);

        var created = await incomingWebhookMessageRepository.TryCreateAsync(message, cancellationToken);
        if (created)
        {
            return IncomingWebhookCaptureResult.Captured(message.Id, normalizedProviderMessageId, normalizedIdempotencyKey);
        }

        var existing = await incomingWebhookMessageRepository.GetByProviderMessageIdForUpdateAsync(
            tenantId.Value,
            normalizedProvider,
            normalizedProviderMessageId,
            cancellationToken);
        if (existing is null)
        {
            existing = await incomingWebhookMessageRepository.GetByIdempotencyKeyForUpdateAsync(
                tenantId.Value,
                normalizedProvider,
                normalizedIdempotencyKey,
                cancellationToken);
            if (existing is not null)
            {
                return IncomingWebhookCaptureResult.Duplicate(
                    existing.Id,
                    existing.ProviderMessageId,
                    normalizedIdempotencyKey);
            }

            return IncomingWebhookCaptureResult.Failure(
                StatusCodes.Status409Conflict,
                "Incoming webhook identity conflict",
                "The provider message identity could not be resolved after a uniqueness conflict.",
                "incoming_webhook_identity_conflict");
        }

        var duplicateClassification = existing.ClassifyDuplicate(readResult.PayloadHash, now);
        if (duplicateClassification == IncomingWebhookDuplicateClassification.PayloadConflict)
        {
            await incomingWebhookMessageRepository.SaveChangesAsync(cancellationToken);
            logger.LogWarning(
                "Incoming webhook payload conflict captured for provider {Provider}",
                normalizedProvider);
            return IncomingWebhookCaptureResult.PayloadConflict(
                existing.Id,
                normalizedProviderMessageId,
                normalizedIdempotencyKey);
        }

        logger.LogInformation(
            "Incoming webhook duplicate captured for provider {Provider}",
            normalizedProvider);

        return IncomingWebhookCaptureResult.Duplicate(
            existing.Id,
            normalizedProviderMessageId,
            normalizedIdempotencyKey);
    }

    private static async Task<byte[]> ReadBodyBytesAsync(
        HttpRequest request,
        long maxBodyBytes,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength > maxBodyBytes)
        {
            throw new IOException("Incoming webhook request body exceeded the configured size limit.");
        }

        request.EnableBuffering(BufferThresholdBytes, maxBodyBytes);
        await using var memory = new MemoryStream();
        var buffer = new byte[81920];
        long totalBytes = 0;
        while (true)
        {
            var bytesRead = await request.Body.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            totalBytes += bytesRead;
            if (totalBytes > maxBodyBytes)
            {
                throw new IOException("Incoming webhook request body exceeded the configured size limit.");
            }

            await memory.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        request.Body.Position = 0;
        return memory.ToArray();
    }

    private static IReadOnlyDictionary<string, string> CaptureHeaders(IHeaderDictionary headers)
    {
        Dictionary<string, string> captured = new(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            captured[header.Key] = JoinHeaderValues(header.Key, header.Value);
        }

        return captured;
    }

    private static string JoinHeaderValues(string headerName, StringValues values)
    {
        var separator = string.Equals(headerName, "svix-signature", StringComparison.OrdinalIgnoreCase)
            ? " "
            : ",";
        return string.Join(
            separator,
            values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()));
    }

    private static string ComputePayloadHash(byte[] bodyBytes)
    {
        var hash = SHA256.HashData(bodyBytes);
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string SerializeSafeHeaders(IReadOnlyDictionary<string, string> headers, string? verificationReceipt = null)
    {
        SortedDictionary<string, string> safe = new(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            if (IsSensitiveHeader(header.Key))
            {
                continue;
            }

            safe[header.Key] = Truncate(header.Value, MaxSafeHeaderValueLength);
        }

        if (!string.IsNullOrWhiteSpace(verificationReceipt))
        {
            safe[VerificationReceiptHeader] = Truncate(verificationReceipt.Trim(), MaxSafeHeaderValueLength);
        }

        return JsonSerializer.Serialize(safe);
    }

    private static bool IsSensitiveHeader(string headerName) =>
        SensitiveHeaderFragments.Any(fragment => headerName.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static int MapVerificationStatusCode(string failureCategory)
    {
        if (failureCategory.Contains("not_configured", StringComparison.OrdinalIgnoreCase) ||
            failureCategory.Contains("secret_missing", StringComparison.OrdinalIgnoreCase) ||
            failureCategory.Contains("secret_unresolved", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCodes.Status503ServiceUnavailable;
        }

        if (failureCategory.Contains("body_too_large", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCodes.Status413PayloadTooLarge;
        }

        if (failureCategory.Contains("missing", StringComparison.OrdinalIgnoreCase) ||
            failureCategory.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
            failureCategory.Contains("timestamp", StringComparison.OrdinalIgnoreCase) ||
            failureCategory.Contains("signature", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCodes.Status401Unauthorized;
        }

        return StatusCodes.Status400BadRequest;
    }

    private static string NormalizeFailureCategory(string? failureCategory, string provider)
    {
        if (string.IsNullOrWhiteSpace(failureCategory))
        {
            return $"{provider}_webhook_verification_failed";
        }

        return Truncate(failureCategory.Trim(), 100);
    }

    private static string NormalizeRequired(string value, int maxLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Truncate(value.Trim().ToLowerInvariant(), maxLength);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
