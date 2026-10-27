using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

namespace Explore.Application.Features.ConfigurationManifest.Moderation;

/// <summary>
/// Complete portable meaning and requested support, with no source jurisdiction, native identity,
/// inventory claim, approval, publication state or operational subject. Destination qualification
/// is supplied separately; importing this document can only propose a draft.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ModerationConfigurationDocument
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumDefinitions = 256;

    [JsonConstructor]
    public ModerationConfigurationDocument(
        int schemaVersion, long catalogueRevision, IReadOnlyList<ModerationNativeMode> modes,
        IReadOnlyList<ModerationConfigurationClass> classes,
        IReadOnlyList<ModerationConfigurationReason> reasons,
        IReadOnlyList<ModerationConfigurationProfile> profiles,
        IReadOnlyList<ModerationConfigurationReference> supportedProfiles)
    {
        SchemaVersion = schemaVersion;
        CatalogueRevision = catalogueRevision;
        Modes = Snapshot(modes);
        Classes = Snapshot(classes);
        Reasons = Snapshot(reasons);
        Profiles = Snapshot(profiles);
        SupportedProfiles = Snapshot(supportedProfiles);
    }

    public int SchemaVersion { get; }
    public long CatalogueRevision { get; }
    public IReadOnlyList<ModerationNativeMode> Modes { get; }
    public IReadOnlyList<ModerationConfigurationClass> Classes { get; }
    public IReadOnlyList<ModerationConfigurationReason> Reasons { get; }
    public IReadOnlyList<ModerationConfigurationProfile> Profiles { get; }
    public IReadOnlyList<ModerationConfigurationReference> SupportedProfiles { get; }

    private static IReadOnlyList<T> Snapshot<T>(IReadOnlyList<T> values)
    {
        if (values is null || values.Count > MaximumDefinitions)
            throw new ConfigurationPortabilityContractException(
                values is null ? ConfigurationPortabilityDiagnosticCodes.ContractInvalid
                    : ConfigurationPortabilityDiagnosticCodes.CountExceeded, "$");
        return Array.AsReadOnly(values.ToArray());
    }
}

/// <summary>Semantic code and revision only. The destination explicitly supplies its owning scope.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ModerationConfigurationReference(string Code, long Revision);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ModerationConfigurationClass(
    ModerationConfigurationReference Reference, string Meaning,
    ModerationReferenceGround Ground, ModerationReferenceBasis Basis);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ModerationConfigurationReason(
    ModerationConfigurationReference Reference, string Meaning, ModerationConfigurationReference Class);

/// <summary>
/// Immutable arrays retain complete profile definitions and all base/additive recipes.
/// Timing is a relative ceiling, never a person's sanction interval or event pointer.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ModerationConfigurationProfile(
    ModerationConfigurationReference Rule,
    ModerationConfigurationReference Reason,
    ModerationReferenceProfileId ReferenceProfile,
    long ReferenceRevision,
    ModerationReferenceGrade Grade,
    ImmutableArray<ModerationScopeKind> ScopeKinds,
    ImmutableArray<ModerationSubjectKind> SubjectKinds,
    ImmutableArray<ModerationReferenceEffect> Effects,
    ModerationConsequenceBundleKind Bundle,
    ModerationProtectionPurpose? ProtectionPurpose,
    ImmutableArray<ModerationAdditiveStrategyKind> Strategies,
    ModerationPolicyTiming Timing,
    int RequiredApprovals,
    int ContestationCalendarMonths,
    ModerationReferenceRecognition Recognition,
    ImmutableArray<ModerationConfigurationRecipe> Recipes);

/// <summary>
/// All thirteen neutral consequence dimensions. Safeguards remain domain-owned constants,
/// not writable import flags. Strategy null identifies the base recipe.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ModerationConfigurationRecipe(
    ModerationAdditiveStrategyKind? Strategy,
    ModerationContentMode Content,
    ModerationManagementMode Management,
    ModerationResourceReadMode Resources,
    ModerationNewAdmissionMode NewAdmission,
    ModerationExistingAdmissionMode ExistingAdmission,
    ModerationPrivateDisclosureMode PrivateDisclosure,
    ModerationSettlementMode Settlement,
    ModerationCapacityMode Capacity,
    ModerationCredentialMode Credentials,
    ModerationNotificationMode Notifications,
    ModerationRecognitionMode Recognition,
    ModerationPendingWorkMode PendingWork,
    ModerationRestorationMode Restoration)
{
    internal static ModerationConfigurationRecipe FromNative(
        ModerationConsequenceModes plan, ModerationAdditiveStrategyKind? strategy) =>
        new(strategy, plan.Content, plan.Management, plan.Resources, plan.NewAdmission, plan.ExistingAdmission,
            plan.PrivateDisclosure, plan.Settlement, plan.Capacity, plan.Credentials, plan.Notifications,
            plan.Recognition, plan.PendingWork, plan.Restoration);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ModerationConfigurationDocument))]
internal sealed partial class ModerationConfigurationJsonContext : JsonSerializerContext;
