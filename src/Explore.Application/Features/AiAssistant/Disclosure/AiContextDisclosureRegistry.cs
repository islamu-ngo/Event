using System.Collections.Generic;
using Explore.Application.DTOs.Location;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Features.AiAssistant.Disclosure;

/// <summary>
/// Authoritative registry of persisted public <c>*Pii</c> properties and explicitly
/// purpose-authorized derived projections. Raw PII entries remain independently
/// enumerable so schema completeness checks cannot be weakened by projection entries.
/// </summary>
/// <remarks>
/// <para>
/// <b>Policy hierarchy</b> (CTO correction #7): instance policy ∩ tenant policy ∩ user consent.
/// This registry encodes the <b>base</b> classification. Effective disclosure is computed by
/// <see cref="ResolveEffectiveRule"/> as the intersection of base rule + provider trust tier
/// + Phase-4 gate.
/// </para>
/// <para>
/// <b>Default rule</b>: any <c>*Pii</c> property not present in this registry resolves to
/// <see cref="AiContextDisclosureRuleEnum.Deny"/>. The Phase-1 reflection test
/// (<c>Event.Architecture.Tests/AiContextDisclosureSchemaTests</c>) fails the build if a
/// <c>*Pii</c> property is missing an entry.
/// </para>
/// <para>
/// <b>Downgrade matrix</b> (matches matrix §3.2):
/// <list type="bullet">
///   <item><c>Public</c>/<c>Internal</c> — rule stays constant across all provider-trust tiers.</item>
///   <item><c>Confidential</c> — LocalModel rule applies only at <c>LocalInProcessOrSameNetworkModel</c>; all other tiers downgrade to <c>Deny</c>.</item>
///   <item><c>Restricted</c> — same as Confidential (Local-only).</item>
///   <item><c>Special</c> — <c>Deny</c> at every tier, including Local.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class AiContextDisclosureRegistry
{
    private readonly IReadOnlyDictionary<string, AiContextDisclosureEntry> _entriesByKey;
    private readonly IReadOnlyDictionary<string, AiContextDisclosureEntry> _projectionEntriesByKey;

    private AiContextDisclosureRegistry(
        IReadOnlyDictionary<string, AiContextDisclosureEntry> entriesByKey,
        IReadOnlyDictionary<string, AiContextDisclosureEntry> projectionEntriesByKey)
    {
        _entriesByKey = entriesByKey;
        _projectionEntriesByKey = projectionEntriesByKey;
    }

    /// <summary>
    /// All registered entries. Order is unspecified; consumers must not rely on insertion order.
    /// </summary>
    public IReadOnlyCollection<AiContextDisclosureEntry> Entries => _entriesByKey.Values.ToList();

    /// <summary>
    /// Derived fields whose values are safe only after their owning purpose evaluator has
    /// materialized the named projection. These entries never reclassify the raw PII source.
    /// </summary>
    public IReadOnlyCollection<AiContextDisclosureEntry> ProjectionEntries =>
        _projectionEntriesByKey.Values.ToList();

    /// <summary>
    /// Number of registered entries. Used by the completeness reflection test as a sanity floor.
    /// </summary>
    public int Count => _entriesByKey.Count;

    /// <summary>
    /// Constructs the canonical registry seeded from
    /// <c>field-classification-matrix.md</c> §4. The seed is hand-curated and reviewed;
    /// adding a <c>*Pii</c> property without a matrix row fails the build (Task 1.5).
    /// </summary>
    public static AiContextDisclosureRegistry CreateDefault()
    {
        var entries = new List<AiContextDisclosureEntry>
        {
            // ───────────── UserPii (5 persisted public properties; 1 nav skipped) ─────────────
            new(
                EntityName: nameof(UserPii),
                FieldName: nameof(UserPii.UserId),
                Sensitivity: AiContextSensitivityEnum.Internal,
                LocalModelRule: AiContextDisclosureRuleEnum.Allow,
                Rationale: "Opaque foreign key; safe as an uncorrelated reference at every tier.",
                Phase4Gated: false),
            new(
                EntityName: nameof(UserPii),
                FieldName: nameof(UserPii.Email),
                Sensitivity: AiContextSensitivityEnum.Restricted,
                LocalModelRule: AiContextDisclosureRuleEnum.Allow,
                Rationale: "Direct PII (GDPR Art. 4(1)). Local-model disclosure requires owner self-consent.",
                Phase4Gated: true),
            new(
                EntityName: nameof(UserPii),
                FieldName: nameof(UserPii.FirstName),
                Sensitivity: AiContextSensitivityEnum.Restricted,
                LocalModelRule: AiContextDisclosureRuleEnum.Allow,
                Rationale: "Direct PII. Local-model disclosure requires owner self-consent.",
                Phase4Gated: true),
            new(
                EntityName: nameof(UserPii),
                FieldName: nameof(UserPii.LastName),
                Sensitivity: AiContextSensitivityEnum.Restricted,
                LocalModelRule: AiContextDisclosureRuleEnum.Allow,
                Rationale: "Direct PII. Local-model disclosure requires owner self-consent.",
                Phase4Gated: true),

            // ───────────── OrganizationPii (7 persisted public properties; 1 nav skipped) ─────────────
            new(
                EntityName: nameof(OrganizationPii),
                FieldName: nameof(OrganizationPii.OrganizationId),
                Sensitivity: AiContextSensitivityEnum.Internal,
                LocalModelRule: AiContextDisclosureRuleEnum.Allow,
                Rationale: "Opaque foreign key.",
                Phase4Gated: false),
            new(
                EntityName: nameof(OrganizationPii),
                FieldName: nameof(OrganizationPii.FullName),
                Sensitivity: AiContextSensitivityEnum.Public,
                LocalModelRule: AiContextDisclosureRuleEnum.Allow,
                Rationale: "Public-facing display name; intentionally indexed.",
                Phase4Gated: false),
            new(
                EntityName: nameof(OrganizationPii),
                FieldName: nameof(OrganizationPii.Email),
                Sensitivity: AiContextSensitivityEnum.Confidential,
                LocalModelRule: AiContextDisclosureRuleEnum.Allow,
                Rationale: "Org contact email. Local-model disclosure requires org-admin consent.",
                Phase4Gated: true),
            new(
                EntityName: nameof(OrganizationPii),
                FieldName: nameof(OrganizationPii.Country),
                Sensitivity: AiContextSensitivityEnum.Internal,
                LocalModelRule: AiContextDisclosureRuleEnum.Allow,
                Rationale: "Coarse jurisdiction metadata.",
                Phase4Gated: false),
            new(
                EntityName: nameof(OrganizationPii),
                FieldName: nameof(OrganizationPii.City),
                Sensitivity: AiContextSensitivityEnum.Internal,
                LocalModelRule: AiContextDisclosureRuleEnum.Allow,
                Rationale: "Coarse jurisdiction metadata.",
                Phase4Gated: false),
            new(
                EntityName: nameof(OrganizationPii),
                FieldName: nameof(OrganizationPii.Address),
                Sensitivity: AiContextSensitivityEnum.Restricted,
                LocalModelRule: AiContextDisclosureRuleEnum.Redact,
                Rationale: "Physical address. Local model redacts to City + Postcode; external tiers deny.",
                Phase4Gated: true),
            new(
                EntityName: nameof(OrganizationPii),
                FieldName: nameof(OrganizationPii.Postcode),
                Sensitivity: AiContextSensitivityEnum.Internal,
                LocalModelRule: AiContextDisclosureRuleEnum.Allow,
                Rationale: "Coarse jurisdiction (postal-area granularity).",
                Phase4Gated: false),

            // ───────────── ActorPii (5 persisted public properties; 1 nav skipped) ─────────────
            new(
                EntityName: nameof(ActorPii),
                FieldName: nameof(ActorPii.ActorId),
                Sensitivity: AiContextSensitivityEnum.Internal,
                LocalModelRule: AiContextDisclosureRuleEnum.Allow,
                Rationale: "Opaque foreign key.",
                Phase4Gated: false),
            new(
                EntityName: nameof(ActorPii),
                FieldName: nameof(ActorPii.DisplayName),
                Sensitivity: AiContextSensitivityEnum.Public,
                LocalModelRule: AiContextDisclosureRuleEnum.Allow,
                Rationale: "Public-facing actor display name; intentionally indexed.",
                Phase4Gated: false),
            new(
                EntityName: nameof(AtprotoIdentity),
                FieldName: nameof(AtprotoIdentity.Did),
                Sensitivity: AiContextSensitivityEnum.Public,
                LocalModelRule: AiContextDisclosureRuleEnum.Allow,
                Rationale: "W3C DID — pseudonymous by design (no PII mapping without resolver authority).",
                Phase4Gated: false),
            new(
                EntityName: nameof(AtprotoIdentity),
                FieldName: nameof(AtprotoIdentity.Handle),
                Sensitivity: AiContextSensitivityEnum.Public,
                LocalModelRule: AiContextDisclosureRuleEnum.Allow,
                Rationale: "Public handle (e.g. @handle); intentionally indexed.",
                Phase4Gated: false),
            new(
                EntityName: nameof(ActorPii),
                FieldName: nameof(ActorPii.ProfilePictureUri),
                Sensitivity: AiContextSensitivityEnum.Public,
                LocalModelRule: AiContextDisclosureRuleEnum.Allow,
                Rationale: "Public CDN URL.",
                Phase4Gated: false),

            // ───────────── LocationPii (7 persisted public properties; 1 nav skipped) ─────────────
            new(
                EntityName: nameof(LocationPii),
                FieldName: nameof(LocationPii.LocationId),
                Sensitivity: AiContextSensitivityEnum.Internal,
                LocalModelRule: AiContextDisclosureRuleEnum.Allow,
                Rationale: "Opaque foreign key.",
                Phase4Gated: false),
            new(
                EntityName: nameof(LocationPii),
                FieldName: nameof(LocationPii.Address),
                Sensitivity: AiContextSensitivityEnum.Restricted,
                LocalModelRule: AiContextDisclosureRuleEnum.Redact,
                Rationale: "Physical address. Local model redacts to City + Postcode; external tiers deny.",
                Phase4Gated: true),
            new(
                EntityName: nameof(LocationPii),
                FieldName: nameof(LocationPii.AddressSubstringKey),
                Sensitivity: AiContextSensitivityEnum.Restricted,
                LocalModelRule: AiContextDisclosureRuleEnum.Deny,
                Rationale: "Reversible normalized exact-address search material is persistence-only and must never enter model context.",
                Phase4Gated: false),
            new(
                EntityName: nameof(LocationPii),
                FieldName: nameof(LocationPii.AddressSubstringKeyVersion),
                Sensitivity: AiContextSensitivityEnum.Internal,
                LocalModelRule: AiContextDisclosureRuleEnum.Deny,
                Rationale: "Persistence key-version metadata has no model-context purpose.",
                Phase4Gated: false),
            new(
                EntityName: nameof(LocationPii),
                FieldName: nameof(LocationPii.Postcode),
                Sensitivity: AiContextSensitivityEnum.Restricted,
                LocalModelRule: AiContextDisclosureRuleEnum.Allow,
                Rationale: "A venue postcode may identify a private location; purpose-specific EventLocation disclosure decides when it may be released.",
                Phase4Gated: true),
            new(
                EntityName: nameof(LocationPii),
                FieldName: nameof(LocationPii.Latitude),
                Sensitivity: AiContextSensitivityEnum.Restricted,
                LocalModelRule: AiContextDisclosureRuleEnum.Aggregate,
                Rationale: "Precise geo. Local model bins to city centroid (~1km); external tiers deny.",
                Phase4Gated: true),
            new(
                EntityName: nameof(LocationPii),
                FieldName: nameof(LocationPii.Longitude),
                Sensitivity: AiContextSensitivityEnum.Restricted,
                LocalModelRule: AiContextDisclosureRuleEnum.Aggregate,
                Rationale: "Precise geo. Local model bins to city centroid (~1km); external tiers deny.",
                Phase4Gated: true),
        };

        string[] publicEventLocationFields =
        [
            nameof(EventLocationPublicDto.EventLocationId),
            nameof(EventLocationPublicDto.State),
            nameof(EventLocationPublicFieldsDto.Country),
            nameof(EventLocationPublicFieldsDto.Timezone),
            nameof(EventLocationPublicFieldsDto.City),
            nameof(EventLocationPublicFieldsDto.VenueName),
            nameof(EventLocationPublicFieldsDto.RoomName),
            nameof(EventLocationPublicFieldsDto.StreetAddress),
            nameof(EventLocationPublicFieldsDto.Postcode),
            nameof(EventLocationPublicFieldsDto.Latitude),
            nameof(EventLocationPublicFieldsDto.Longitude),
            nameof(EventLocationPublicFieldsDto.FormattedAddress),
            nameof(EventLocationPublicFieldsDto.MapUrl),
            nameof(EventLocationPublicFieldsDto.Geohash)
        ];
        AiContextDisclosureEntry[] projectionEntries = publicEventLocationFields
            .Select(fieldName => new AiContextDisclosureEntry(
                EntityName: nameof(EventLocationPublicDto),
                FieldName: fieldName,
                Sensitivity: AiContextSensitivityEnum.Public,
                LocalModelRule: AiContextDisclosureRuleEnum.Allow,
                Rationale: "The EventLocation public-purpose evaluator already selected this field; raw LocationPii remains separately restricted.",
                Phase4Gated: false))
            .ToArray();

        var byKey = new Dictionary<string, AiContextDisclosureEntry>(entries.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (!byKey.TryAdd(entry.Key, entry))
            {
                throw new InvalidOperationException(
                    $"Duplicate AI disclosure entry for key '{entry.Key}'. Each *Pii field must be registered exactly once.");
            }
        }

        var projectionsByKey = new Dictionary<string, AiContextDisclosureEntry>(
            projectionEntries.Length,
            StringComparer.OrdinalIgnoreCase);
        foreach (AiContextDisclosureEntry entry in projectionEntries)
        {
            if (byKey.ContainsKey(entry.Key) || !projectionsByKey.TryAdd(entry.Key, entry))
            {
                throw new InvalidOperationException(
                    $"Duplicate AI disclosure entry for key '{entry.Key}'. Each field must be registered exactly once.");
            }
        }

        return new AiContextDisclosureRegistry(byKey, projectionsByKey);
    }

    /// <summary>
    /// Attempts to retrieve the entry for a given <c>*Pii</c> entity property.
    /// Returns <c>false</c> for navigation properties and unregistered fields (fail-closed).
    /// </summary>
    public bool TryGetEntry(string entityName, string fieldName, out AiContextDisclosureEntry entry)
    {
        string key = AiContextDisclosureEntry.BuildKey(entityName, fieldName);
        return _entriesByKey.TryGetValue(key, out entry!)
            || _projectionEntriesByKey.TryGetValue(key, out entry!);
    }

    /// <summary>
    /// Resolves the effective disclosure rule for a single field at a given provider-trust tier.
    /// Applies the matrix §3.2 downgrade rules:
    /// <list type="bullet">
    ///   <item>Unregistered field → <c>Deny</c>.</item>
    ///   <item><c>Special</c> sensitivity → <c>Deny</c> at every tier.</item>
    ///   <item><c>Confidential</c>/<c>Restricted</c> + non-Local tier → <c>Deny</c>.</item>
    ///   <item><c>Phase4Gated</c> + PII disclosure disabled → <c>Deny</c>.</item>
    ///   <item>Otherwise → entry's <see cref="AiContextDisclosureEntry.LocalModelRule"/>.</item>
    /// </list>
    /// </summary>
    /// <param name="piiDisclosureEnabled">
    /// Whether Phase 4 has been verified and the runtime PII-disclosure flag is on (Task 4.4).
    /// Until then, callers MUST pass <c>false</c>.
    /// </param>
    public AiContextDisclosureRuleEnum ResolveEffectiveRule(
        string entityName,
        string fieldName,
        AiProviderTrustTierEnum providerTrustTier,
        bool piiDisclosureEnabled)
    {
        if (!TryGetEntry(entityName, fieldName, out AiContextDisclosureEntry? entry))
        {
            return AiContextDisclosureRuleEnum.Deny;
        }

        return ResolveEffectiveRule(entry, providerTrustTier, piiDisclosureEnabled);
    }

    /// <summary>
    /// Overload that accepts an already-resolved entry. Useful when the caller iterates the registry.
    /// </summary>
    public AiContextDisclosureRuleEnum ResolveEffectiveRule(
        AiContextDisclosureEntry entry,
        AiProviderTrustTierEnum providerTrustTier,
        bool piiDisclosureEnabled)
    {
        // Phase-4 gate (CTO correction #5): PII disclosure disabled until Tasks 4.1–4.4 verified.
        if (entry.Phase4Gated && !piiDisclosureEnabled)
        {
            return AiContextDisclosureRuleEnum.Deny;
        }

        // Special-category data is denied at every tier, including Local.
        if (entry.Sensitivity == AiContextSensitivityEnum.Special)
        {
            return AiContextDisclosureRuleEnum.Deny;
        }

        // Public / Internal: rule is constant across all provider-trust tiers.
        if (entry.Sensitivity <= AiContextSensitivityEnum.Internal)
        {
            return entry.LocalModelRule;
        }

        // Confidential / Restricted: only the Local model retains the LocalModelRule.
        // All external tiers (tenant-private endpoint, tenant-external, platform-external, Unknown)
        // downgrade to Deny. Unknown is the most restrictive and cannot elevate above Deny here.
        return providerTrustTier == AiProviderTrustTierEnum.LocalInProcessOrSameNetworkModel
            ? entry.LocalModelRule
            : AiContextDisclosureRuleEnum.Deny;
    }
}
