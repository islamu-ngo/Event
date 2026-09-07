using Explore.Domain.Enums;

namespace Explore.Application.Features.AiAssistant.Disclosure;

/// <summary>
/// Represents the disclosure classification for a persisted <c>*Pii</c> property or a
/// purpose-authorized derived projection field. Entries are immutable and seeded by
/// <see cref="AiContextDisclosureRegistry.CreateDefault"/>. The effective rule for
/// any provider-trust tier is computed by
/// <see cref="AiContextDisclosureRegistry.ResolveEffectiveRule"/>.
/// </summary>
/// <param name="EntityName">
/// Unqualified entity or projection name (e.g. <c>UserPii</c>). Match is OrdinalIgnoreCase.
/// </param>
/// <param name="FieldName">
/// Public property name on the entity or flattened derived projection (e.g. <c>Email</c>).
/// Navigation properties are not registered.
/// </param>
/// <param name="Sensitivity">
/// Base data sensitivity tier (<see cref="AiContextSensitivityEnum"/>). Higher = more restrictive.
/// </param>
/// <param name="LocalModelRule">
/// Most-permissive disclosure rule, granted only at the most trusted provider tier
/// (<see cref="AiProviderTrustTierEnum.LocalInProcessOrSameNetworkModel"/>). Less-trusted
/// tiers downgrade automatically per the matrix §3.2 downgrade matrix.
/// </param>
/// <param name="Rationale">Short, auditable justification for the classification.</param>
/// <param name="Phase4Gated">
/// When <c>true</c>, the entry is forcibly <see cref="AiContextDisclosureRuleEnum.Deny"/>
/// until Phase 4 (Tasks 4.1–4.4) is verified and the runtime PII-disclosure flag is enabled.
/// See <c>dev/active/ai-context-disclosure-policy/field-classification-matrix.md</c> §8.
/// </param>
public sealed record AiContextDisclosureEntry(
    string EntityName,
    string FieldName,
    AiContextSensitivityEnum Sensitivity,
    AiContextDisclosureRuleEnum LocalModelRule,
    string Rationale,
    bool Phase4Gated = false)
{
    /// <summary>
    /// Stable lookup key (<c>EntityName.FieldName</c>, InvariantCultureIgnoreCase).
    /// </summary>
    public string Key => BuildKey(EntityName, FieldName);

    /// <summary>
    /// Builds the canonical lookup key. Centralised so callers (registry, reflection test)
    /// cannot drift on the format.
    /// </summary>
    public static string BuildKey(string entityName, string fieldName)
        => $"{entityName}.{fieldName}";
}
