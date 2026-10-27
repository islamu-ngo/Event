using System.Collections.Immutable;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Services.Moderation;

public enum ModerationNativeMode
{
    Embedded = 1,
    RemoteStrict = 2
}

/// <summary>Source observations only. None of these values is runtime conformance or an activation permit.</summary>
public enum ModerationSourceSupport
{
    Unimplemented = 1,
    Partial = 2,
    ImplementedUnverified = 3
}

public enum ModerationConsumerFreshness
{
    Mutation = 1,
    PrivateDisclosure = 2,
    PublicSnapshot = 3,
    ExternalHandoff = 4,
    ControlPlane = 5,
    RemoteReceiver = 6
}

/// <summary>
/// The native participant must implement every named alias, resolver and checkpoint in this contract.
/// The inventory declares the obligation; copying this descriptor does not establish conformance.
/// </summary>
public sealed record ModerationConsumerContract(
    string Id,
    string SubjectResolver,
    string TargetResolver,
    string AuthorizationOwner,
    string EntryCheckpoint,
    string TerminalCheckpoint,
    string WriterFence,
    ModerationConsumerFreshness FreshnessClass,
    string OwningPacket,
    string ConformanceTest);

public sealed record ModerationConsumerInventoryEntry(
    ModerationConsumerContract Contract,
    ModerationSourceSupport SupportState,
    string SupportNotes,
    ImmutableArray<string> SourcePaths,
    ImmutableArray<string> Aliases);

public sealed record ModerationModeInventoryEntry(
    ModerationNativeMode Mode, ModerationSourceSupport SupportState, string Notes);

/// <summary>
/// Exact proposed publication coordinates, not an action authorization. Protected remedy capabilities
/// can be matched for native coverage but cannot become restrictive Domain policy effects.
/// </summary>
public sealed record ModerationActivationBinding(
    Guid PolicyId,
    long PolicyRevision,
    Guid PublicationId,
    AuthorityScope Jurisdiction,
    ModerationRuleReference Rule,
    ModerationCapability Capability,
    ModerationNativeMode Mode);

public enum ModerationNativeConformance
{
    NotRun = 1,
    Failed = 2,
    Passed = 3
}

/// <summary>
/// A trusted native participant's conformance evidence for this exact publication and runtime source
/// revision. VerifiedAt is the publication writer's current qualification observation, not a historic
/// test completion timestamp. Historic test receipts must be requalified before producing this input;
/// their finite UTC ValidUntil remains a bound even when VerifiedAt is refreshed.
/// </summary>
public sealed record ModerationCapabilityRegistration(
    ModerationActivationBinding Binding,
    string InventoryFingerprint,
    ModerationConsumerContract Contract,
    Guid SourceId,
    long SourceRevision,
    string WriterGeneration,
    ModerationNativeConformance Conformance,
    DateTimeOffset VerifiedAt,
    DateTimeOffset ValidUntil);

/// <summary>
/// Independently read current native registration source. The writer reads these facts from the
/// owning authority, not from policy payloads or the registration's own historic receipt. Its fence
/// must remain held through publication; its finite UTC expiry bounds the whole qualification.
/// This value neither owns nor replaces that fence.
/// </summary>
public sealed record ModerationCapabilitySourceObservation(
    ModerationActivationBinding Binding,
    string ConsumerId,
    Guid SourceId,
    long Revision,
    string WriterGeneration,
    DateTimeOffset ObservedAt,
    DateTimeOffset ValidUntil);

public enum ModerationInventoryMismatch
{
    InvalidBinding,
    InvalidObservationTime,
    MissingConsumer,
    ExtraConsumer,
    UnknownConsumer,
    DuplicateConsumer,
    MissingCurrentSource,
    ExtraCurrentSource,
    DuplicateCurrentSource,
    ReusedSourceIdentity,
    BindingMismatch,
    InventoryMismatch,
    ContractMismatch,
    ConformanceNotPassed,
    StaleObservation,
    InvalidSource,
    SourceMismatch,
    ExpiredSourceAuthority,
    ExpiredConformanceAuthority
}

public sealed record ModerationInventoryIssue(ModerationInventoryMismatch Kind, string? ConsumerId = null);

/// <summary>
/// Evidence comparison only. A match is not readiness, prerequisite qualification, ordinary access,
/// a remote permit or permission to publish. Publication retains all applicable native writer fences.
/// </summary>
public sealed class ModerationInventoryMatch
{
    internal ModerationInventoryMatch(
        ImmutableArray<string> requiredConsumers,
        ImmutableArray<ModerationInventoryIssue> issues,
        ImmutableArray<ModerationCapabilityRegistration> registrations)
    {
        RequiredConsumers = requiredConsumers;
        Issues = issues;
        Registrations = issues.IsEmpty ? registrations : [];
    }

    public bool Matches => Issues.IsEmpty;
    public ImmutableArray<string> RequiredConsumers { get; }
    public ImmutableArray<ModerationInventoryIssue> Issues { get; }
    public ImmutableArray<ModerationCapabilityRegistration> Registrations { get; }
}
