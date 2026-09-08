using Explore.Domain.Enums;

namespace Explore.Application.Features.AiAssistant.Disclosure;

/// <summary>
/// Redacts field values that should not be persisted in transcripts or logs.
/// Used as a safety net alongside <see cref="IAiContextGateway"/>: the gateway
/// sanitizes values before they reach the LLM prompt; the redactor ensures that
/// any value persisted (transcript, log, telemetry) is also scrubbed.
/// </summary>
public interface IAiContextRedactor
{
    /// <summary>
    /// Redacts a single field value based on its disclosure classification.
    /// Fields classified as <see cref="AiContextSensitivityEnum.Restricted"/>
    /// or higher (and not explicitly allowed via <paramref name="piiDisclosureEnabled"/>)
    /// are replaced with a redaction marker.
    /// </summary>
    /// <param name="entityName">The registry entity name (e.g. <c>UserPii</c>).</param>
    /// <param name="fieldName">The registry field name (e.g. <c>Email</c>).</param>
    /// <param name="rawValue">The raw value that may contain PII.</param>
    /// <param name="piiDisclosureEnabled">Whether PII disclosure is enabled at the caller level.</param>
    /// <returns>The redacted value, or the original value if disclosure is permitted.</returns>
    string? RedactFieldValue(string entityName, string fieldName, string? rawValue, bool piiDisclosureEnabled);

    /// <summary>
    /// Redacts all known PII patterns from an arbitrary text block (e.g. a transcript
    /// message or log entry). This is a pattern-based sweep that does NOT rely on
    /// field-level registry knowledge — it catches email addresses, phone numbers,
    /// and other common PII patterns that may have been embedded in free-text.
    /// </summary>
    /// <param name="text">The text to scrub.</param>
    /// <returns>The scrubbed text with PII patterns replaced.</returns>
    string RedactEmbeddedPii(string? text);
}
