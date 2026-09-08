using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Explore.API.ExceptionHandling;
using Explore.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Explore.API.Services;

public sealed class CoopWebhookSignatureValidator(
    IOptionsMonitor<CoopProviderOptions> options,
    ILogger<CoopWebhookSignatureValidator> logger) : ICoopWebhookSignatureValidator
{
    private const int BufferThresholdBytes = 30 * 1024;
    private const string SignaturePrefix = "sha256";
    private const string VersionedSignaturePrefix = "v1";

    public async Task<CoopWebhookSignatureValidationResult> ReadAndValidateAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var currentOptions = options.CurrentValue;
        if (string.IsNullOrWhiteSpace(currentOptions.WebhookSecret))
        {
            logger.LogWarning("Coop webhook rejected because Reporting:Coop:WebhookSecret is not configured.");
            return Failure(
                StatusCodes.Status503ServiceUnavailable,
                "Coop webhook is not configured",
                ApiProblemTypes.ServiceUnavailable,
                "The Coop webhook shared secret is not configured.",
                "coop_webhook_not_configured");
        }

        if (!TryGetSingleHeader(request, currentOptions.WebhookTimestampHeaderName, out var timestampHeader) ||
            !TryParseTimestamp(timestampHeader, out var timestamp))
        {
            return Failure(
                StatusCodes.Status401Unauthorized,
                "Invalid Coop webhook timestamp",
                ApiProblemTypes.Unauthorized,
                "The Coop webhook timestamp is missing or invalid.",
                "coop_webhook_timestamp_invalid");
        }

        var tolerance = TimeSpan.FromSeconds(Math.Clamp(currentOptions.WebhookToleranceSeconds, 30, 86_400));
        if (DateTimeOffset.UtcNow - timestamp > tolerance || timestamp - DateTimeOffset.UtcNow > tolerance)
        {
            return Failure(
                StatusCodes.Status401Unauthorized,
                "Stale Coop webhook timestamp",
                ApiProblemTypes.Unauthorized,
                "The Coop webhook timestamp is outside the accepted tolerance window.",
                "coop_webhook_timestamp_stale");
        }

        if (!TryGetSingleHeader(request, currentOptions.WebhookSignatureHeaderName, out var signatureHeader))
        {
            return Failure(
                StatusCodes.Status401Unauthorized,
                "Missing Coop webhook signature",
                ApiProblemTypes.Unauthorized,
                "The Coop webhook signature header is required.",
                "coop_webhook_signature_missing");
        }

        byte[] bodyBytes;
        try
        {
            bodyBytes = await ReadBodyBytesAsync(request, currentOptions.WebhookMaxBodyBytes, cancellationToken);
        }
        catch (IOException)
        {
            return Failure(
                StatusCodes.Status413PayloadTooLarge,
                "Coop webhook body is too large",
                ApiProblemTypes.PayloadTooLarge,
                "The Coop webhook body exceeds the configured size limit.",
                "coop_webhook_body_too_large");
        }

        if (bodyBytes.Length == 0)
        {
            return Failure(
                StatusCodes.Status400BadRequest,
                "Empty Coop webhook body",
                ApiProblemTypes.BadRequest,
                "The Coop webhook body is required.",
                "coop_webhook_body_empty");
        }

        if (!HasValidSignature(
                currentOptions.WebhookSecret,
                timestampHeader,
                bodyBytes,
                signatureHeader))
        {
            logger.LogWarning("Coop webhook rejected because signature verification failed.");
            return Failure(
                StatusCodes.Status401Unauthorized,
                "Invalid Coop webhook signature",
                ApiProblemTypes.Unauthorized,
                "The Coop webhook signature could not be verified.",
                "coop_webhook_signature_invalid");
        }

        return CoopWebhookSignatureValidationResult.Success(Encoding.UTF8.GetString(bodyBytes));
    }

    private static async Task<byte[]> ReadBodyBytesAsync(
        HttpRequest request,
        long maxBodyBytes,
        CancellationToken cancellationToken)
    {
        request.EnableBuffering(BufferThresholdBytes, maxBodyBytes);
        await using var memory = new MemoryStream();
        await request.Body.CopyToAsync(memory, cancellationToken);
        request.Body.Position = 0;
        return memory.ToArray();
    }

    private static bool HasValidSignature(
        string webhookSecret,
        string timestampHeader,
        byte[] bodyBytes,
        string signatureHeader)
    {
        var expected = ComputeSignature(webhookSecret.Trim(), timestampHeader.Trim(), bodyBytes);
        return ExtractSignatureCandidates(signatureHeader)
            .Select(TryDecodeSignature)
            .Any(candidate => candidate is not null && CryptographicOperations.FixedTimeEquals(expected, candidate));
    }

    private static byte[] ComputeSignature(
        string webhookSecret,
        string timestampHeader,
        byte[] bodyBytes)
    {
        var secretBytes = Encoding.UTF8.GetBytes(webhookSecret);
        var timestampBytes = Encoding.UTF8.GetBytes(timestampHeader);
        var payload = new byte[timestampBytes.Length + 1 + bodyBytes.Length];
        Buffer.BlockCopy(timestampBytes, 0, payload, 0, timestampBytes.Length);
        payload[timestampBytes.Length] = (byte)'.';
        Buffer.BlockCopy(bodyBytes, 0, payload, timestampBytes.Length + 1, bodyBytes.Length);

        using var hmac = new HMACSHA256(secretBytes);
        return hmac.ComputeHash(payload);
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

    private static bool TryGetSingleHeader(
        HttpRequest request,
        string headerName,
        out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(headerName) ||
            !request.Headers.TryGetValue(headerName.Trim(), out StringValues values))
        {
            return false;
        }

        value = values.FirstOrDefault()?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
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

    private static CoopWebhookSignatureValidationResult Failure(
        int statusCode,
        string title,
        string type,
        string detail,
        string code) =>
        CoopWebhookSignatureValidationResult.Failure(statusCode, title, type, detail, code);
}
