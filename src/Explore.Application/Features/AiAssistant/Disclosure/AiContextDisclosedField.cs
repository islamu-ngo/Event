using Explore.Domain.Enums;

namespace Explore.Application.Features.AiAssistant.Disclosure;

/// <summary>
/// One sanitized field within an <see cref="AiContextSanitizedEnvelope"/>.
/// <see cref="AppliedRule"/> records which disclosure rule governed the value:
/// Allow (verbatim), Redact (masked), Aggregate (binned), or Deny (the field is
/// absent from <see cref="AiContextSanitizedEnvelope.DisclosedFields"/> entirely).
/// </summary>
public sealed record AiContextDisclosedField(
    string Name,
    object? Value,
    AiContextDisclosureRuleEnum AppliedRule);
