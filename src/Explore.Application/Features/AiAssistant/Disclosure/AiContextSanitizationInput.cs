using Explore.Domain.Enums;

namespace Explore.Application.Features.AiAssistant.Disclosure;

/// <summary>
/// Immutable request envelope passed to <see cref="IAiContextGateway.Sanitize"/>.
/// Contains everything the gateway needs to compute the effective disclosure per field:
/// the entity fields, the resolved provider trust tier, the viewer scope (public/
/// organizer-team/instance-admin), the set of consent-granted field keys, and the
/// global PII disclosure switch (Phase 4 gate, Task 4.4).
/// </summary>
public sealed record AiContextSanitizationInput(
    string EntityName,
    IReadOnlyDictionary<string, object?> Fields,
    AiProviderTrustTierEnum ProviderTrustTier,
    AiViewerScopeEnum ViewerScope,
    IReadOnlySet<string> GrantedFieldKeys,
    bool PiiDisclosureEnabled,
    AiContextSensitivityEnum MaxSensitivity = AiContextSensitivityEnum.Public);
