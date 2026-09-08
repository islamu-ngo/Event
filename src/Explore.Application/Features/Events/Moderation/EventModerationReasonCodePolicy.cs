using Explore.Application.Features.Events.Requests.Commands;

namespace Explore.Application.Features.Events.Moderation;

public sealed record EventModerationReasonMetadata(string ReasonCode, string? CorrelationId);

public static class EventModerationReasonCodePolicy
{
    public const int MaxReasonCodeLength = 100;
    public const int MaxCorrelationIdLength = 100;
    public const string InvalidReasonCodeFailureCode = "event_moderation_reason_code_invalid";
    public const string InvalidCorrelationIdFailureCode = "event_moderation_correlation_id_invalid";

    public static bool TryNormalizeLight(
        string? reasonCode,
        string? correlationId,
        out EventModerationReasonMetadata metadata,
        out string? failureCode,
        out string? error)
        => TryNormalize(reasonCode, correlationId, ModerateEventCommand.DefaultReasonCode, out metadata, out failureCode, out error);

    public static bool TryNormalizeHeavy(
        string? reasonCode,
        string? correlationId,
        out EventModerationReasonMetadata metadata,
        out string? failureCode,
        out string? error)
        => TryNormalize(reasonCode, correlationId, HeavyRedactEventCommand.DefaultReasonCode, out metadata, out failureCode, out error);

    public static bool TryNormalizeUnmoderation(
        string? reasonCode,
        string? correlationId,
        out EventModerationReasonMetadata metadata,
        out string? failureCode,
        out string? error)
        => TryNormalize(reasonCode, correlationId, UnmoderateEventCommand.DefaultReasonCode, out metadata, out failureCode, out error);

    private static bool TryNormalize(
        string? reasonCode,
        string? correlationId,
        string defaultReasonCode,
        out EventModerationReasonMetadata metadata,
        out string? failureCode,
        out string? error)
    {
        metadata = new EventModerationReasonMetadata(defaultReasonCode, null);
        failureCode = null;
        error = null;

        var normalizedReasonCode = string.IsNullOrWhiteSpace(reasonCode)
            ? defaultReasonCode
            : reasonCode.Trim();

        if (normalizedReasonCode.Length > MaxReasonCodeLength || !IsReasonCodeShape(normalizedReasonCode))
        {
            failureCode = InvalidReasonCodeFailureCode;
            error = "ReasonCode must be a lowercase machine code up to 100 characters using letters, numbers, and underscores.";
            return false;
        }

        string? normalizedCorrelationId = null;
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            normalizedCorrelationId = correlationId.Trim();
            if (normalizedCorrelationId.Length > MaxCorrelationIdLength)
            {
                failureCode = InvalidCorrelationIdFailureCode;
                error = "CorrelationId cannot exceed 100 characters.";
                return false;
            }
        }

        metadata = new EventModerationReasonMetadata(normalizedReasonCode, normalizedCorrelationId);
        return true;
    }

    private static bool IsReasonCodeShape(string reasonCode)
    {
        var previousWasSeparator = false;
        for (var i = 0; i < reasonCode.Length; i++)
        {
            var character = reasonCode[i];
            if (character == '_')
            {
                if (i == 0 || i == reasonCode.Length - 1 || previousWasSeparator)
                {
                    return false;
                }

                previousWasSeparator = true;
                continue;
            }

            if ((character is < 'a' or > 'z') && (character is < '0' or > '9'))
            {
                return false;
            }

            previousWasSeparator = false;
        }

        return reasonCode.Length > 0;
    }
}
