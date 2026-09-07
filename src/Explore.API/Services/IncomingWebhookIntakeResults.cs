using Explore.API.ExceptionHandling;
using Explore.Application.Contracts.Webhooks;
using Microsoft.AspNetCore.Http;

namespace Explore.API.Services;

public sealed record IncomingWebhookReadResult(
    bool Succeeded,
    string Provider,
    string? RawPayload,
    ReadOnlyMemory<byte> RawPayloadBytes,
    DateTimeOffset ReceivedAt,
    string? PayloadHash,
    string ContentType,
    string ContentEncoding,
    IReadOnlyDictionary<string, string> Headers,
    IncomingWebhookVerificationResult? Verification,
    int StatusCode,
    string Title,
    string Type,
    string Detail,
    string Code)
{
    public static IncomingWebhookReadResult Success(
        string provider,
        string rawPayload,
        ReadOnlyMemory<byte> rawPayloadBytes,
        DateTimeOffset receivedAt,
        string payloadHash,
        string contentType,
        string contentEncoding,
        IReadOnlyDictionary<string, string> headers,
        IncomingWebhookVerificationResult verification) =>
        new(true, provider, rawPayload, rawPayloadBytes.ToArray(), receivedAt, payloadHash, contentType, contentEncoding, headers, verification, StatusCodes.Status200OK, string.Empty, string.Empty, string.Empty, string.Empty);

    public static IncomingWebhookReadResult Failure(
        string provider,
        int statusCode,
        string title,
        string detail,
        string code) =>
        new(false, provider, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.MinValue, null, string.Empty, string.Empty, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), null, statusCode, title, ProblemType(statusCode), detail, code);

    private static string ProblemType(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => ApiProblemTypes.BadRequest,
        StatusCodes.Status401Unauthorized => ApiProblemTypes.Unauthorized,
        StatusCodes.Status404NotFound => ApiProblemTypes.NotFound,
        StatusCodes.Status409Conflict => ApiProblemTypes.Conflict,
        StatusCodes.Status413PayloadTooLarge => ApiProblemTypes.PayloadTooLarge,
        StatusCodes.Status503ServiceUnavailable => ApiProblemTypes.ServiceUnavailable,
        _ => ApiProblemTypes.BadRequest
    };
}

public sealed record IncomingWebhookCaptureResult(
    IncomingWebhookCaptureOutcome Outcome,
    Guid MessageId,
    string ProviderMessageId,
    string IdempotencyKey,
    int StatusCode,
    string Title,
    string Type,
    string Detail,
    string Code)
{
    public bool Succeeded => Outcome is IncomingWebhookCaptureOutcome.Captured or IncomingWebhookCaptureOutcome.Duplicate;
    public bool IsDuplicate => Outcome == IncomingWebhookCaptureOutcome.Duplicate;
    public bool IsPayloadConflict => Outcome == IncomingWebhookCaptureOutcome.PayloadConflict;

    public static IncomingWebhookCaptureResult Captured(
        Guid messageId,
        string providerMessageId,
        string idempotencyKey) =>
        new(IncomingWebhookCaptureOutcome.Captured, messageId, providerMessageId, idempotencyKey, StatusCodes.Status200OK, string.Empty, string.Empty, string.Empty, string.Empty);

    public static IncomingWebhookCaptureResult Duplicate(
        Guid messageId,
        string providerMessageId,
        string idempotencyKey) =>
        new(IncomingWebhookCaptureOutcome.Duplicate, messageId, providerMessageId, idempotencyKey, StatusCodes.Status200OK, string.Empty, string.Empty, string.Empty, string.Empty);

    public static IncomingWebhookCaptureResult PayloadConflict(
        Guid messageId,
        string providerMessageId,
        string idempotencyKey) =>
        new(
            IncomingWebhookCaptureOutcome.PayloadConflict,
            messageId,
            providerMessageId,
            idempotencyKey,
            StatusCodes.Status409Conflict,
            "Incoming webhook payload conflict",
            ApiProblemTypes.Conflict,
            "The provider message identity was already used with different payload bytes.",
            "incoming_webhook_payload_conflict");

    public static IncomingWebhookCaptureResult Failure(
        int statusCode,
        string title,
        string detail,
        string code) =>
        new(IncomingWebhookCaptureOutcome.Failed, Guid.Empty, string.Empty, string.Empty, statusCode, title, IncomingWebhookReadResult.Failure("incoming", statusCode, title, detail, code).Type, detail, code);
}

public enum IncomingWebhookCaptureOutcome
{
    Captured = 1,
    Duplicate = 2,
    PayloadConflict = 3,
    Failed = 4
}
