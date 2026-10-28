using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using ISLAMU.Moderation.Domain;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

namespace Explore.Application.Features.ConfigurationManifest.Moderation;

/// <summary>
/// A native caller's explicit destination mapping, never serialized into the document.
/// Context and IDs are compiler inputs, not evidence of current caller or publication authority.
/// Profile identities must be resolved by the destination writer, including retained definitions.
/// </summary>
public sealed record ModerationConfigurationDestinationMapping
{
    public ModerationConfigurationDestinationMapping(
        Guid policyId, long expectedRevision, Guid publicationId, ModerationPolicyCompilationContext context,
        IReadOnlyDictionary<ModerationConfigurationReference, Guid> profileIds)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(profileIds);
        if (profileIds.Count > ModerationConfigurationDocument.MaximumDefinitions)
            throw new ConfigurationPortabilityContractException(ConfigurationPortabilityDiagnosticCodes.CountExceeded, "$");
        PolicyId = policyId;
        ExpectedRevision = expectedRevision;
        PublicationId = publicationId;
        Context = context;
        ProfileIds = profileIds.ToFrozenDictionary();
    }

    public Guid PolicyId { get; }
    public long ExpectedRevision { get; }
    public Guid PublicationId { get; }
    public ModerationPolicyCompilationContext Context { get; }
    public IReadOnlyDictionary<ModerationConfigurationReference, Guid> ProfileIds { get; }
}

/// <summary>
/// Source-generated strict JSON and authority stripping/rebinding. This unit has no persistence,
/// authority resolver or activation path; the parent manifest adapter owns those integrations.
/// </summary>
public static class ModerationConfigurationDocumentProjection
{
    private static readonly ModerationConfigurationJsonContext JsonContext = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        MaxDepth = ConfigurationPortabilityContentLimits.MaximumJsonDepth
    });

    public static ModerationConfigurationDocument Parse(ReadOnlyMemory<byte> artifact)
    {
        if (artifact.IsEmpty)
            Fail(ConfigurationPortabilityDiagnosticCodes.ContractInvalid);
        if (artifact.Length > ConfigurationPortabilityContentLimits.MaximumArtifactUtf8Bytes)
            Fail(ConfigurationPortabilityDiagnosticCodes.TooLarge);
        try
        {
            using var json = JsonDocument.Parse(artifact, new JsonDocumentOptions
            {
                MaxDepth = ConfigurationPortabilityContentLimits.MaximumJsonDepth,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
            Inspect(json.RootElement);
            var value = JsonSerializer.Deserialize(artifact.Span, JsonContext.ModerationConfigurationDocument)
                ?? throw new JsonException();
            ModerationConfigurationDocumentValidator.ValidateShape(value);
            return value;
        }
        catch (JsonException)
        {
            // Do not expose hostile input, member names or deserializer payloads in diagnostics.
            throw new ConfigurationPortabilityContractException(ConfigurationPortabilityDiagnosticCodes.ContractInvalid, "$");
        }
    }

    public static byte[] Serialize(ModerationConfigurationDocument document)
    {
        ModerationConfigurationDocumentValidator.ValidateShape(document);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonContext.ModerationConfigurationDocument);
        if (bytes.Length > ConfigurationPortabilityContentLimits.MaximumArtifactUtf8Bytes)
            Fail(ConfigurationPortabilityDiagnosticCodes.TooLarge);
        using var json = JsonDocument.Parse(bytes);
        Inspect(json.RootElement);
        return bytes;
    }

    /// <summary>Export only a compiler-produced complete draft; no native UUID is retained.</summary>
    public static ModerationConfigurationDocument FromCompiledDraft(ModerationPolicyCompiledDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var state = draft.Proposed;
        return new(ModerationConfigurationDocument.CurrentSchemaVersion, state.CatalogueRevision,
            state.Modes,
            state.Classes.Select(item => new ModerationConfigurationClass(Portable(item.Reference),
                item.Meaning, item.Ground, item.Basis)).ToArray(),
            state.Reasons.Select(item => new ModerationConfigurationReason(Portable(item.Reference),
                item.Meaning, Portable(item.Class))).ToArray(),
            draft.Profiles.Select(item =>
            {
                var definition = item.Definition;
                return new ModerationConfigurationProfile(Portable(definition.Rule), Portable(definition.Reason),
                    definition.ReferenceProfile, definition.ReferenceRevision, definition.Grade,
                    definition.ScopeKinds, definition.SubjectKinds, definition.Effects, definition.Bundle,
                    definition.ProtectionPurpose, definition.Strategies, definition.Timing,
                    definition.RequiredApprovals, definition.ContestationCalendarMonths, definition.Recognition,
                    Recipes(item));
            }).ToArray(), state.SupportedProfiles.Select(Portable).ToArray());
    }

    /// <summary>
    /// Assigns every semantic owner to the explicitly supplied destination and every profile to its
    /// exact native identity. Inventory coordinates come only from the destination registry.
    /// Full policy/history/bounds validation follows in ValidateOnly, not in this shape projection.
    /// </summary>
    public static ModerationPolicyProposedState ToProposedState(
        ModerationConfigurationDocument document, ModerationConfigurationDestinationMapping mapping,
        ModerationCapabilityRegistry registry)
    {
        ModerationConfigurationDocumentValidator.ValidateShape(document);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(registry);
        if (mapping.Context.Bounds?.Jurisdiction is not { } scope
            || mapping.ProfileIds.Count != document.Profiles.Count
            || document.Profiles.Any(item => !mapping.ProfileIds.ContainsKey(item.Rule))
            || mapping.ProfileIds.Values.Any(id => id == Guid.Empty || id.Version != 7 || id.Variant is < 8 or > 11)
            || mapping.ProfileIds.Values.Distinct().Count() != mapping.ProfileIds.Count)
            throw new ConfigurationPortabilityContractException(ConfigurationPortabilityDiagnosticCodes.ScopeInvalid, "$");
        ModerationRuleReference Native(ModerationConfigurationReference reference)
        {
            try
            {
                return ModerationRuleReference.Create(scope.Id, reference.Code, reference.Revision);
            }
            catch (ArgumentException)
            {
                throw new ConfigurationPortabilityContractException(ConfigurationPortabilityDiagnosticCodes.ContractInvalid, "$");
            }
        }
        return new(mapping.PolicyId, mapping.ExpectedRevision, mapping.PublicationId, scope,
            document.CatalogueRevision, registry.Revision, registry.Fingerprint, ModerationPolicyRequestedState.Draft,
            document.Modes.ToImmutableArray(),
            document.Classes.Select(item => new ModerationPolicyClassDefinition(
                Native(item.Reference), item.Meaning, item.Ground, item.Basis)).ToImmutableArray(),
            document.Reasons.Select(item => new ModerationPolicyReasonDefinition(
                Native(item.Reference), item.Meaning, Native(item.Class))).ToImmutableArray(),
            document.Profiles.Select(item => new ModerationPolicyProfileDefinition(
                mapping.ProfileIds[item.Rule], Native(item.Rule), Native(item.Reason), item.ReferenceProfile,
                item.ReferenceRevision, item.Grade, item.ScopeKinds, item.SubjectKinds, item.Effects,
                item.Bundle, item.ProtectionPurpose, item.Strategies, item.Timing,
                item.RequiredApprovals, item.ContestationCalendarMonths, item.Recognition)).ToImmutableArray(),
            document.SupportedProfiles.Select(Native).ToImmutableArray());
    }

    internal static ImmutableArray<ModerationConfigurationRecipe> Recipes(ModerationPolicyCompiledProfile profile) =>
        profile.Consequences.Select((plan, index) => ModerationConfigurationRecipe.FromNative(plan,
            index == 0 ? null : profile.Definition.Strategies.Order().ElementAt(index - 1))).ToImmutableArray();

    private static ModerationConfigurationReference Portable(ModerationRuleReference reference) =>
        new(reference.Code, reference.Revision);

    private static void Inspect(JsonElement element, string? member = null)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    Fail(ConfigurationPortabilityDiagnosticCodes.ContractInvalid);
                Inspect(property.Value, property.Name);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            if (element.GetArrayLength() > ModerationConfigurationDocument.MaximumDefinitions)
                Fail(ConfigurationPortabilityDiagnosticCodes.CountExceeded);
            foreach (var item in element.EnumerateArray())
                Inspect(item, member);
        }
        else if (element.ValueKind == JsonValueKind.Number)
        {
            // Enum ordinals and floating-point aliases are not portable semantic identifiers.
            if (member is not ("schemaVersion" or "catalogueRevision" or "revision"
                    or "referenceRevision" or "requiredApprovals" or "contestationCalendarMonths")
                || !element.TryGetInt64(out _))
                Fail(ConfigurationPortabilityDiagnosticCodes.ContractInvalid);
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString()!;
            if (text.Length > 4000)
                Fail(ConfigurationPortabilityDiagnosticCodes.StringTooLong);
            if (member is not ("code" or "meaning" or "duration")
                && (text.Length == 0 || !char.IsAsciiLetter(text[0])))
                Fail(ConfigurationPortabilityDiagnosticCodes.ContractInvalid);
        }
    }

    private static void Fail(string code) => throw new ConfigurationPortabilityContractException(code, "$");
}
