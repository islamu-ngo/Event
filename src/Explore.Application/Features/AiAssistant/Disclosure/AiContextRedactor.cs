using System.Text.RegularExpressions;
using Explore.Domain.Enums;

namespace Explore.Application.Features.AiAssistant.Disclosure;

/// <summary>
/// Default redactor that combines field-level registry classification with
/// pattern-based PII scrubbing for free-text. Field-level redaction denies
/// any field whose effective rule (given provider trust and PII gate) is
/// <see cref="AiContextDisclosureRuleEnum.Deny"/> or
/// <see cref="AiContextDisclosureRuleEnum.Redact"/>. Pattern-based redaction
/// catches PII embedded in free-text (transcripts, logs) regardless of field origin.
/// </summary>
public sealed partial class AiContextRedactor : IAiContextRedactor
{
    private readonly AiContextDisclosureRegistry _registry;

    public AiContextRedactor() : this(AiContextDisclosureRegistry.CreateDefault()) { }

    public AiContextRedactor(AiContextDisclosureRegistry registry) => _registry = registry;

    public string? RedactFieldValue(string entityName, string fieldName, string? rawValue, bool piiDisclosureEnabled)
    {
        if (string.IsNullOrEmpty(rawValue))
            return rawValue;

        if (!_registry.TryGetEntry(entityName, fieldName, out var entry))
            return RedactEmbeddedPii(rawValue);

        var rule = entry.Phase4Gated && !piiDisclosureEnabled
            ? AiContextDisclosureRuleEnum.Deny
            : entry.Sensitivity switch
            {
                >= AiContextSensitivityEnum.Restricted => piiDisclosureEnabled
                    ? AiContextDisclosureRuleEnum.Deny
                    : AiContextDisclosureRuleEnum.Deny,
                AiContextSensitivityEnum.Confidential => piiDisclosureEnabled
                    ? AiContextDisclosureRuleEnum.Redact
                    : AiContextDisclosureRuleEnum.Deny,
                _ => AiContextDisclosureRuleEnum.Allow,
            };

        return rule switch
        {
            AiContextDisclosureRuleEnum.Allow => rawValue,
            AiContextDisclosureRuleEnum.Redact => MaskValue(rawValue),
            _ => "[REDACTED]",
        };
    }

    public string RedactEmbeddedPii(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        var result = EmailPattern().Replace(text, "[EMAIL]");
        result = PhonePattern().Replace(result, "[PHONE]");
        return result;
    }

    private static string MaskValue(string value)
    {
        if (value.Length <= 2)
            return "[REDACTED]";
        return string.Concat(value.AsSpan(0, 1), new string('*', value.Length - 2), value.AsSpan(value.Length - 1));
    }

    [GeneratedRegex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", RegexOptions.Compiled)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"\b\d{3}[-.]?\d{3}[-.]?\d{4}\b", RegexOptions.Compiled)]
    private static partial Regex PhonePattern();
}
