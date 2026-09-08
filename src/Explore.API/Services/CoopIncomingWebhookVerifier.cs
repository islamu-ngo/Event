using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Webhooks;
using Explore.Domain;
using Explore.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Explore.API.Services;

public sealed class CoopIncomingWebhookVerifier(
    IOptionsMonitor<CoopProviderOptions> options,
    ITenantContextAccessor tenantContextAccessor,
    ILogger<CoopIncomingWebhookVerifier> logger) : IIncomingWebhookVerifier
{
    private const string SignaturePrefix = "sha256";
    private const string VersionedSignaturePrefix = "v1";

    public string Provider => "coop";

    public Task<IncomingWebhookVerificationResult> VerifyAsync(
        IncomingWebhookContext context,
        CancellationToken cancellationToken)
    {
        var currentOptions = options.CurrentValue;
        if (string.IsNullOrWhiteSpace(currentOptions.WebhookSecret))
        {
            logger.LogWarning("Coop webhook rejected because Reporting:Coop:WebhookSecret is not configured.");
            return Task.FromResult(IncomingWebhookVerificationResult.Rejected(
                "coop_webhook_not_configured",
                "The Coop webhook shared secret is not configured."));
        }

        if (!TryGetHeader(context.Headers, currentOptions.WebhookTimestampHeaderName, out var timestampHeader) ||
            !TryParseTimestamp(timestampHeader, out var timestamp))
        {
            return Task.FromResult(IncomingWebhookVerificationResult.Rejected(
                "coop_webhook_timestamp_invalid",
                "The Coop webhook timestamp is missing or invalid."));
        }

        var tolerance = TimeSpan.FromSeconds(Math.Clamp(currentOptions.WebhookToleranceSeconds, 30, 86_400));
        if (DateTimeOffset.UtcNow - timestamp > tolerance || timestamp - DateTimeOffset.UtcNow > tolerance)
        {
            return Task.FromResult(IncomingWebhookVerificationResult.Rejected(
                "coop_webhook_timestamp_stale",
                "The Coop webhook timestamp is outside the accepted tolerance window."));
        }

        if (!TryGetHeader(context.Headers, currentOptions.WebhookSignatureHeaderName, out var signatureHeader))
        {
            return Task.FromResult(IncomingWebhookVerificationResult.Rejected(
                "coop_webhook_signature_missing",
                "The Coop webhook signature header is required."));
        }

        if (!HasValidSignature(currentOptions.WebhookSecret, timestampHeader, context.RawPayloadBytes.Span, signatureHeader))
        {
            logger.LogWarning("Coop webhook rejected because signature verification failed.");
            return Task.FromResult(IncomingWebhookVerificationResult.Rejected(
                "coop_webhook_signature_invalid",
                "The Coop webhook signature could not be verified."));
        }

        var tenantId = tenantContextAccessor.TenantId;
        if (tenantId is null || tenantId == Guid.Empty)
        {
            return Task.FromResult(IncomingWebhookVerificationResult.Rejected(
                "coop_webhook_tenant_authority_missing",
                "The authenticated Coop credential is not bound to a tenant."));
        }

        if (!TryResolveProviderDecisionId(context.RawPayloadBytes, out var providerMessageId))
        {
            return Task.FromResult(IncomingWebhookVerificationResult.Rejected(
                "coop_provider_decision_id_missing",
                "A signed Coop callback requires a provider decision identifier."));
        }

        if (providerMessageId.Length > IncomingWebhookEffectOutbox.MaxProviderDecisionIdLength)
        {
            return Task.FromResult(IncomingWebhookVerificationResult.Rejected(
                "coop_provider_decision_id_invalid",
                "The provider decision identifier exceeds the allowed size."));
        }

        return Task.FromResult(IncomingWebhookVerificationResult.VerifiedTenantCredential(
            tenantId.Value,
            providerMessageId,
            "moderation.coop.decision",
            providerMessageId));
    }

    private static bool HasValidSignature(
        string webhookSecret,
        string timestampHeader,
        ReadOnlySpan<byte> rawPayload,
        string signatureHeader)
    {
        var expected = ComputeSignature(webhookSecret.Trim(), timestampHeader.Trim(), rawPayload);
        return ExtractSignatureCandidates(signatureHeader)
            .Select(TryDecodeSignature)
            .Any(candidate => candidate is not null && CryptographicOperations.FixedTimeEquals(expected, candidate));
    }

    private static byte[] ComputeSignature(
        string webhookSecret,
        string timestampHeader,
        ReadOnlySpan<byte> rawPayload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
        var prefix = Encoding.UTF8.GetBytes($"{timestampHeader}.");
        var signedContent = new byte[prefix.Length + rawPayload.Length];
        prefix.CopyTo(signedContent, 0);
        rawPayload.CopyTo(signedContent.AsSpan(prefix.Length));
        return hmac.ComputeHash(signedContent);
    }

    private static bool TryResolveProviderDecisionId(ReadOnlyMemory<byte> rawPayload, out string providerDecisionId)
    {
        providerDecisionId = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(rawPayload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            return TryGetRequiredString(document.RootElement, "providerDecisionId", out providerDecisionId) ||
                   TryGetRequiredString(document.RootElement, "provider_decision_id", out providerDecisionId);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetRequiredString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString()?.Trim() ?? string.Empty;
        return value.Length > 0;
    }

    private static IEnumerable<string> ExtractSignatureCandidates(string headerValue)
    {
        foreach (var part in headerValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = part.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex < 0)
            {
                yield return part;
                continue;
            }

            var key = part[..separatorIndex].Trim();
            if (key.Equals(SignaturePrefix, StringComparison.OrdinalIgnoreCase) ||
                key.Equals(VersionedSignaturePrefix, StringComparison.OrdinalIgnoreCase))
            {
                yield return part[(separatorIndex + 1)..].Trim();
            }
        }
    }

    private static byte[]? TryDecodeSignature(string signature)
    {
        if (TryDecodeHex(signature, out var hexBytes))
        {
            return hexBytes;
        }

        try
        {
            return Convert.FromBase64String(signature);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool TryDecodeHex(string value, out byte[] bytes)
    {
        bytes = [];
        if (value.Length != 64)
        {
            return false;
        }

        bytes = new byte[32];
        for (var index = 0; index < bytes.Length; index++)
        {
            var high = FromHex(value[index * 2]);
            var low = FromHex(value[index * 2 + 1]);
            if (high < 0 || low < 0)
            {
                bytes = [];
                return false;
            }

            bytes[index] = (byte)((high << 4) | low);
        }

        return true;
    }

    private static int FromHex(char value) => value switch
    {
        >= '0' and <= '9' => value - '0',
        >= 'a' and <= 'f' => value - 'a' + 10,
        >= 'A' and <= 'F' => value - 'A' + 10,
        _ => -1
    };

    private static bool TryGetHeader(
        IReadOnlyDictionary<string, string> headers,
        string headerName,
        out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(headerName))
        {
            return false;
        }

        return headers.TryGetValue(headerName.Trim(), out value!) && !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryParseTimestamp(string value, out DateTimeOffset timestamp)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds))
        {
            try
            {
                timestamp = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                timestamp = default;
                return false;
            }
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out timestamp);
    }
}
