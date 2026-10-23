using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Explore.Application.Features.Moderation.Policy;

public enum ModerationPolicyRequestedState { Draft = 1, Active = 2 }
public enum ModerationPolicyCompilationOutcome
{
    Invalid = 1, Draft = 2, ActiveRejected = 3, RequiresNativePublicationQualification = 4
}

public enum ModerationPolicyDiagnosticKind
{
    InvalidShape = 1, StaleBinding, DuplicateDefinition, UnknownProfile, UnsupportedEffect,
    IncompatibleMeaning, MeaningRevisionConflict, MissingHistoricalDefinition, OutsideAuthority,
    InvalidResponse, InvalidProtectionPurpose, IncompletePlan, UnsupportedConsumer,
    UnsupportedMode, NativeQualificationRequired, InvalidContribution, IrreversibleCauseConflict
}

public enum ModerationPolicyDiagnosticSeverity { Error = 1, ActivationBlocker = 2, PublicationObligation = 3 }

public sealed record ModerationPolicyDiagnostic(
    ModerationPolicyDiagnosticKind Kind, ModerationPolicyDiagnosticSeverity Severity,
    ModerationRuleReference? Rule = null, string? ConsumerId = null, ModerationNativeMode? Mode = null);

/// <summary>Semantic revisions, not local lookup IDs or translated display labels.</summary>
public sealed record ModerationPolicyClassDefinition(
    ModerationRuleReference Reference, string Meaning,
    ModerationReferenceGround Ground, ModerationReferenceBasis Basis);

public sealed record ModerationPolicyReasonDefinition(
    ModerationRuleReference Reference, string Meaning, ModerationRuleReference Class);

/// <summary>
/// Explicit set ceilings, duration ceilings and procedural floors. These are compilation inputs,
/// not grants. Publication must independently read their current owning authority under its fence.
/// </summary>
public sealed record ModerationPolicyBounds(
    AuthorityScope Jurisdiction,
    ImmutableArray<ModerationScopeKind> ScopeKinds,
    ImmutableArray<ModerationSubjectKind> SubjectKinds,
    ImmutableArray<ModerationReferenceEffect> Effects,
    ImmutableArray<ModerationReferenceTimingKind> TimingKinds,
    TimeSpan MaximumFiniteDuration,
    TimeSpan MaximumReviewDelay,
    int MinimumApprovals,
    int MaximumApprovals,
    int MinimumContestationCalendarMonths);

public sealed record ModerationPolicyTiming(ModerationReferenceTimingKind Kind, TimeSpan Duration);

/// <summary>
/// One complete named response, not a capability subset of a reference profile. Disabled definitions
/// remain in the snapshot for historical meaning; selecting support is separate from defining it.
/// </summary>
public sealed record ModerationPolicyProfileDefinition(
    Guid Id,
    ModerationRuleReference Rule,
    ModerationRuleReference Reason,
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
    ModerationReferenceRecognition Recognition);

/// <summary>
/// Complete authorable state. ExpectedRevision zero means initial adoption. Publication always
/// advances revision; replaying an older definition never rewinds decisions or restores history.
/// No conformance receipts, approval booleans, runtime scripts or operational subjects are importable.
/// </summary>
public sealed record ModerationPolicyProposedState(
    Guid PolicyId,
    long ExpectedRevision,
    Guid PublicationId,
    AuthorityScope Jurisdiction,
    long CatalogueRevision,
    long InventoryRevision,
    string InventoryFingerprint,
    ModerationPolicyRequestedState RequestedState,
    ImmutableArray<ModerationNativeMode> Modes,
    ImmutableArray<ModerationPolicyClassDefinition> Classes,
    ImmutableArray<ModerationPolicyReasonDefinition> Reasons,
    ImmutableArray<ModerationPolicyProfileDefinition> Profiles,
    ImmutableArray<ModerationRuleReference> SupportedProfiles);

/// <summary>
/// Supplied by the native snapshot reader, separately from the authorable payload. Its identity,
/// revision and lifetime must be requalified by the publication writer; a pure compiler cannot
/// establish that this is current or that the caller owns the jurisdiction.
/// </summary>
public sealed record ModerationPolicyCompilationContext(
    Guid HostBindingId, Guid AuthoritySourceId, long AuthorityRevision,
    string WriterGeneration, ModerationPolicyBounds Bounds,
    ModerationPolicyCompiledDraft? Current);

/// <summary>Exact expected coordinates to reread and fence before the atomic publication.</summary>
public sealed record ModerationPolicyPublicationRequirement(
    Guid HostBindingId, Guid AuthoritySourceId, long AuthorityRevision, string WriterGeneration,
    Guid PolicyId, long ExpectedRevision, long ProposedRevision, Guid PublicationId,
    AuthorityScope Jurisdiction, long CatalogueRevision, long InventoryRevision,
    string InventoryFingerprint);

public enum ModerationPolicySemanticOwner
{
    Notice = 1, Publication, Resource, Management, Enrollment, Participant,
    Disclosure, CredentialRecovery, FormAndIncident, AgeEligibility, Classification, Automation
}

/// <summary>
/// A fine-grained operation contract, including its actual resolver selector and complete native
/// consumer contracts. Capability is only the inventory envelope; it is never a substitute handler.
/// Effects without a capability still require their semantic owner and target resolver.
/// </summary>
public sealed record ModerationPolicyEffectRequirement(
    ModerationRuleReference Rule, ModerationNativeMode Mode,
    ModerationReferenceEffect Effect, ModerationPolicySemanticOwner Owner,
    ImmutableArray<ModerationConsumerContract> Consumers);

/// <summary>All thirteen modes, including explicit no-change modes, must be owned and requalified.</summary>
public sealed record ModerationPolicyConsequenceRequirement(
    ModerationRuleReference Rule, ModerationNativeMode Mode,
    ModerationConsequenceBundleKind Bundle, ModerationProtectionPurpose? ProtectionPurpose,
    ModerationAdditiveStrategyKind? Strategy, ModerationConsequenceModes Plan);

public sealed record ModerationPolicyConsumerRequirement(
    ModerationActivationBinding Binding, ImmutableArray<ModerationConsumerContract> Consumers);

public sealed record ModerationPolicyPrerequisiteRequirement(
    ModerationRuleReference Rule, ModerationNativeMode Mode,
    ImmutableArray<ModerationActivationPrerequisite> ActivationGates,
    ImmutableArray<ModerationReferencePrerequisite> NativePrerequisites);

public enum ModerationPolicyPreviewDisposition { Blocked = 1, RequiresNativeQualification = 2 }

/// <summary>Complete profile/mode explanation of a proposed snapshot, never a readiness certificate or permit.</summary>
public sealed class ModerationPolicyProfilePreview
{
    internal ModerationPolicyProfilePreview(
        ModerationPolicyPublicationRequirement publication, ModerationPolicyPrerequisiteRequirement prerequisites,
        ImmutableArray<ModerationPolicyConsumerRequirement> consumers,
        ImmutableArray<ModerationPolicyEffectRequirement> effects,
        ImmutableArray<ModerationPolicyConsequenceRequirement> consequences,
        ImmutableArray<ModerationPolicyDiagnostic> diagnostics)
    {
        Publication = publication;
        Prerequisites = prerequisites;
        Consumers = consumers;
        Effects = effects;
        Consequences = consequences;
        Diagnostics = diagnostics;
        Disposition = diagnostics.Any(item => item.Severity is ModerationPolicyDiagnosticSeverity.Error
            or ModerationPolicyDiagnosticSeverity.ActivationBlocker)
            ? ModerationPolicyPreviewDisposition.Blocked
            : ModerationPolicyPreviewDisposition.RequiresNativeQualification;
    }

    public ModerationPolicyPublicationRequirement Publication { get; }
    public ModerationPolicyPrerequisiteRequirement Prerequisites { get; }
    public ImmutableArray<ModerationPolicyConsumerRequirement> Consumers { get; }
    public ImmutableArray<ModerationPolicyEffectRequirement> Effects { get; }
    public ImmutableArray<ModerationPolicyConsequenceRequirement> Consequences { get; }
    public ImmutableArray<ModerationPolicyDiagnostic> Diagnostics { get; }
    public ModerationPolicyPreviewDisposition Disposition { get; }
}

/// <summary>
/// A compiled definition preserves its purpose, response and all plans. NativeOwner effects must
/// never be projected to general sanctions, even if their capability envelope overlaps a denial.
/// </summary>
public sealed record ModerationPolicyCompiledProfile
{
    internal ModerationPolicyCompiledProfile(
        ModerationPolicyProfileDefinition definition, ModerationReferenceProfile reference,
        ImmutableArray<ModerationConsequenceModes> consequences)
        => (Definition, Reference, Consequences) = (definition, reference, consequences);

    public ModerationPolicyProfileDefinition Definition { get; }
    public ModerationReferenceProfile Reference { get; }
    public ImmutableArray<ModerationConsequenceModes> Consequences { get; }
}

/// <summary>
/// Pure immutable draft. It contains no native evidence, active masks or approval certificates.
/// The internal constructor prevents consumers from labeling arbitrary partial arrays as compiled.
/// </summary>
public sealed class ModerationPolicyCompiledDraft
{
    internal ModerationPolicyCompiledDraft(
        ModerationPolicyProposedState proposed, ModerationPolicyPublicationRequirement publication,
        ModerationPolicyBounds bounds,
        ImmutableArray<ModerationPolicyCompiledProfile> profiles,
        ImmutableArray<ModerationPolicyConsumerRequirement> consumers,
        ImmutableArray<ModerationPolicyEffectRequirement> effects,
        ImmutableArray<ModerationPolicyConsequenceRequirement> consequences,
        ImmutableArray<ModerationPolicyPrerequisiteRequirement> prerequisites)
        => (Proposed, Publication, Bounds, Profiles, Consumers, Effects, Consequences, Prerequisites) =
            (proposed, publication, bounds, profiles, consumers, effects, consequences, prerequisites);

    public ModerationPolicyProposedState Proposed { get; }
    public ModerationPolicyPublicationRequirement Publication { get; }
    public ModerationPolicyBounds Bounds { get; }
    public ImmutableArray<ModerationPolicyCompiledProfile> Profiles { get; }
    public ImmutableArray<ModerationPolicyConsumerRequirement> Consumers { get; }
    public ImmutableArray<ModerationPolicyEffectRequirement> Effects { get; }
    public ImmutableArray<ModerationPolicyConsequenceRequirement> Consequences { get; }
    public ImmutableArray<ModerationPolicyPrerequisiteRequirement> Prerequisites { get; }
}

public sealed class ModerationPolicyCompilationResult
{
    internal ModerationPolicyCompilationResult(
        ModerationPolicyCompilationOutcome outcome, ModerationPolicyCompiledDraft? draft,
        ImmutableArray<ModerationPolicyDiagnostic> diagnostics)
    {
        (Outcome, Draft, Diagnostics) = (outcome, draft, diagnostics);
        Previews = draft is null ? [] : draft.Prerequisites
            .OrderBy(item => item.Rule.Code, StringComparer.Ordinal).ThenBy(item => item.Rule.Revision)
            .ThenBy(item => item.Mode)
            .Select(item => new ModerationPolicyProfilePreview(draft.Publication, item,
                draft.Consumers.Where(required => required.Binding.Rule == item.Rule
                    && required.Binding.Mode == item.Mode).ToImmutableArray(),
                draft.Effects.Where(required => required.Rule == item.Rule
                    && required.Mode == item.Mode).ToImmutableArray(),
                draft.Consequences.Where(required => required.Rule == item.Rule
                    && required.Mode == item.Mode).ToImmutableArray(),
                diagnostics.Where(issue => (issue.Rule is null || issue.Rule == item.Rule)
                    && (issue.Mode is null || issue.Mode == item.Mode)).ToImmutableArray()))
            .ToImmutableArray();
    }

    public ModerationPolicyCompilationOutcome Outcome { get; }
    public ModerationPolicyCompiledDraft? Draft { get; }
    public ImmutableArray<ModerationPolicyDiagnostic> Diagnostics { get; }
    public ImmutableArray<ModerationPolicyProfilePreview> Previews { get; }
}
