using Explore.Domain.Enums;

namespace Explore.Application.Features.AiAssistant.Disclosure;

/// <summary>
/// Immutable sanitized envelope produced by <see cref="IAiContextGateway.Sanitize"/>.
/// Exposes only the fields the caller is permitted to see under the current viewer
/// scope, provider trust tier, and consent state. Fields that were redacted or denied
/// are listed by name for audit/transcript purposes — their values are NEVER carried.
/// </summary>
public sealed record AiContextSanitizedEnvelope(
    string EntityName,
    IReadOnlyList<AiContextDisclosedField> DisclosedFields,
    IReadOnlyList<string> RedactedFieldNames,
    IReadOnlyList<string> DeniedFieldNames,
    string? FailureCode,
    string? FailureMessage)
{
    /// <summary>True when the gateway produced a usable envelope (no failure code).</summary>
    public bool Succeeded => string.IsNullOrEmpty(FailureCode);

    /// <summary>Builds a successful envelope with disclosed/redacted/denied partitions.</summary>
    public static AiContextSanitizedEnvelope Success(
        string entityName,
        IReadOnlyList<AiContextDisclosedField> disclosed,
        IReadOnlyList<string> redacted,
        IReadOnlyList<string> denied)
        => new(
            EntityName: entityName,
            DisclosedFields: disclosed,
            RedactedFieldNames: redacted,
            DeniedFieldNames: denied,
            FailureCode: null,
            FailureMessage: null);

    /// <summary>Builds a failed envelope (e.g. unregistered entity, gateway exception).</summary>
    public static AiContextSanitizedEnvelope Failed(string entityName, string code, string message)
        => new(
            EntityName: entityName,
            DisclosedFields: Array.Empty<AiContextDisclosedField>(),
            RedactedFieldNames: Array.Empty<string>(),
            DeniedFieldNames: Array.Empty<string>(),
            FailureCode: code,
            FailureMessage: message);
}
