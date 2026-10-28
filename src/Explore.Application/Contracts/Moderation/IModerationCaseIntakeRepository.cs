using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Contracts.Moderation;

public enum ModerationCaseAccessPurpose
{
    SubmitAllegation = 1,
    ReadAllegation = 2
}

/// <summary>
/// Untrusted purpose intent. CaseId and ExpectedCaseRevision are read-only coordinates and must
/// be empty/zero for submission; the native writer resolves the canonical occurrence internally.
/// Tenant, caller, scope and relationship never come from this request.
/// </summary>
public sealed record ModerationCaseAccessRequest(
    ModerationCaseAccessPurpose Purpose, Guid CaseId, Guid OperationId, long ExpectedCaseRevision,
    Guid ReceiptId, ReadOnlyMemory<byte> PresentedToken)
{
    private byte[] narrative = [];
    public ReadOnlyMemory<byte> Narrative
    {
        get => narrative.ToArray();
        init => narrative = value.ToArray();
    }
    public override string ToString() => nameof(ModerationCaseAccessRequest);
}

/// <summary>
/// Stages case provenance in the caller's serializable transaction. The caller consumes the
/// authenticated receipt and stages its qualified acknowledgement in that transaction, saves, then calls
/// RecheckStagedAsync immediately before commit. A false recheck requires rollback. A returned
/// entity is not a committed acknowledgement or permission to serialize confidential case data.
/// </summary>
public interface IModerationCaseIntakeRepository
{
    Task<ModerationCaseReadSourcePlan?> DiscoverReadSourcesAsync(
        ModerationCaseAccessRequest request, CancellationToken cancellationToken);
    Task<ModerationCaseIntakeStage?> StageAsync(ModerationCaseAccessRequest request, CancellationToken cancellationToken);
    Task<bool> RecheckStagedAsync(CancellationToken cancellationToken);
    Task<ModerationCase?> ReadAsync(ModerationCaseAccessRequest request, CancellationToken cancellationToken);
    Task<bool> RecheckReadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Transaction-local entities, not an API response. Receipt is attached to the same native
/// context as Case so the caller's Consume and acknowledgement writes share one SaveChanges.
/// A consumed receipt identifies an exact replay; the caller must not capture another acknowledgement.
/// </summary>
public sealed record ModerationCaseIntakeStage(ModerationCase Case, ModerationCaseTargetReceipt Receipt)
{
    public override string ToString() => nameof(ModerationCaseIntakeStage);
}

/// <summary>
/// Mandatory native target/relationship owner, separate from receipt authentication. Acquire
/// must fence and reread the actual typed target, source, relationship and caller purpose on
/// transactionId. It must not turn receipt coordinates, private-row existence or provider
/// judgment into permission. ReadAllegation requires an independently qualified read purpose;
/// the submit receipt alone never supplies it. Recheck rereads only already-held source fences.
/// No attribution writer is part of this contract.
/// </summary>
public interface IModerationCaseTargetProofOwner
{
    Task<ModerationCaseReadSourcePlan?> DiscoverReadSourcesAsync(
        ModerationCaseAccessRequest request, CancellationToken cancellationToken);
    Task<ModerationCaseTargetObservation?> AcquireAsync(
        ModerationCaseAccessRequest request, ModerationCaseTargetReceipt authenticatedReceipt,
        Guid transactionId, DateTimeOffset observedAt, CancellationToken cancellationToken);

    Task<ModerationCaseTargetObservation?> RecheckAsync(
        ModerationCaseTargetObservation acquired, DateTimeOffset observedAt, CancellationToken cancellationToken);
}

/// <summary>
/// Transaction-local lock coordinates, not authenticated read or custody authority.
/// The native owner retains the complete original metadata and requalifies it under the held fences.
/// </summary>
public sealed record ModerationCaseReadSourcePlan(
    Guid TransactionId, Guid CallerUserId, Guid ReceiptId, Guid HostBindingId,
    Guid MaintenanceServicePrincipalId, AuthorityScope Scope, ModerationCaseTargetKind TargetKind,
    Guid IssuerUserId, Guid IssuerRoleGrantId, bool IssuerPlatformGrant,
    Guid? EventId, Guid? PublisherActorId, Guid? OrganizerActorId)
{
    public override string ToString() => nameof(ModerationCaseReadSourcePlan);
}

/// <summary>
/// An observation returned only by the injected native owner, not an input accepted from an API.
/// Every coordinate is compared against the authenticated receipt and current transaction.
/// </summary>
public sealed record ModerationCaseTargetObservation(
    Guid TransactionId, ModerationCaseAccessPurpose Purpose, Guid CaseId, Guid OperationId,
    long ExpectedCaseRevision, Guid ReceiptId, Guid CallerUserId, Guid HostBindingId,
    Guid QualificationEpochId, long QualificationRevision, Guid RelationshipSourceId,
    ModerationCaseRelationshipGeneration RelationshipGeneration, AuthorityScope Scope, ModerationEvidenceReference Evidence,
    DateTimeOffset ObservedAt, DateTimeOffset ValidUntil)
{
    public override string ToString() => nameof(ModerationCaseTargetObservation);
}

/// <summary>
/// Untrusted lookup and allegation material, not target coordinates or attributed identity.
/// HiddenContentReceipt uses NativeKind to select the actual protected source owner.
/// LocalIncident names the event as its native source, never an alleged person's identity.
/// EventId is absent for standalone actor/service sources; it never names a synthetic parent.
/// </summary>
public sealed record ModerationCaseTargetIssueRequest(
    ModerationCaseTargetKind Kind, ModerationCaseTargetKind NativeKind, Guid? EventId, Guid NativeTargetId,
    ModerationCaseRelationshipKind RelationshipKind, Guid RelationshipSourceId,
    ReadOnlyMemory<byte> OccurrenceMaterial, ReadOnlyMemory<byte> Material)
{
    public override string ToString() => nameof(ModerationCaseTargetIssueRequest);
}

/// <summary>Transaction-local token material. Publish only after Save, final recheck and commit.</summary>
public sealed record ModerationCaseIssuedReceipt(ModerationCaseTargetReceipt Receipt, ReadOnlyMemory<byte> Token)
{
    public override string ToString() => nameof(ModerationCaseIssuedReceipt);
}

/// <summary>
/// Native writers require the caller's primary serializable transaction. They stage only;
/// SaveChanges and a successful RecheckIssuanceAsync immediately before commit are mandatory.
/// Read grants and lifetime adoption require current native administrative authority.
/// </summary>
public interface IModerationCaseTargetIssuer
{
    Task<ModerationCaseIssuedReceipt?> IssueSubmissionAsync(
        ModerationCaseTargetIssueRequest request, CancellationToken cancellationToken);
    Task<ModerationCaseIssuedReceipt?> IssueReadAsync(Guid readGrantId, CancellationToken cancellationToken);
    Task<ModerationCaseTargetPolicy?> SetLifetimePolicyAsync(ModerationCaseTargetKind kind,
        long expectedRevision, TimeSpan submissionLifetime, TimeSpan readLifetime, bool enabled,
        CancellationToken cancellationToken);
    Task<ModerationCaseReadGrant?> GrantReadAsync(
        ModerationCaseReadGrantRequest request, CancellationToken cancellationToken);
    Task<ModerationCaseReadGrant?> RevokeReadAsync(
        Guid grantId, Guid operationId, long expectedRevision, CancellationToken cancellationToken);
    Task<bool> RecheckIssuanceAsync(CancellationToken cancellationToken);
}
