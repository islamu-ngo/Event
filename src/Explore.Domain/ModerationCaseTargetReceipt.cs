using System.Security.Cryptography;
using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

/// <summary>
/// Digest-only, account-bound submit or independently granted read purpose. Native issuance
/// must qualify the actual relationship and source under commit fences; these coordinates
/// neither disclose the protected target payload nor establish responsible-human attribution.
/// </summary>
public sealed class ModerationCaseTargetReceipt
{
    private ModerationCaseTargetReceipt() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CallerUserId { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid QualificationEpochId { get; private set; }
    public long QualificationRevision { get; private set; }
    public Guid RelationshipSourceId { get; private set; }
    public ModerationCaseRelationshipKind RelationshipKind { get; private set; }
    public long? RelationshipRevision { get; private set; }
    public Guid? RelationshipConcurrencyStamp { get; private set; }
    public string? RelationshipFactsDigest { get; private set; }
    public ModerationCaseRelationshipGeneration RelationshipGeneration =>
        RelationshipKind == ModerationCaseRelationshipKind.InstanceAdministrator
            ? ModerationCaseRelationshipGeneration.FromIdentity(RelationshipSourceId)
        : RelationshipConcurrencyStamp is { } stamp
            ? ModerationCaseRelationshipGeneration.FromStamp(stamp)
            : ModerationCaseRelationshipGeneration.FromRevision(RelationshipRevision ?? 0);
    public Guid LifetimePolicyId { get; private set; }
    public long LifetimePolicyRevision { get; private set; }
    public string TokenDigest { get; private set; } = string.Empty;
    public ModerationCaseTargetKind TargetKind { get; private set; }
    public Guid TargetId { get; private set; }
    public Guid TargetVersion { get; private set; }
    public ModerationScopeKind TargetOriginKind { get; private set; }
    public Guid TargetOriginTrustDomainId { get; private set; }
    public Guid? TargetOriginSolutionId { get; private set; }
    public Guid? TargetOriginInstanceId { get; private set; }
    public Guid? TargetOriginTenantId { get; private set; }
    public Guid? TargetOriginCommunityId { get; private set; }
    public Guid? TargetOriginResourceId { get; private set; }
    public Guid? TargetOriginEventId { get; private set; }
    public ModerationScopeKind SourceOriginKind { get; private set; }
    public Guid SourceOriginTrustDomainId { get; private set; }
    public Guid? SourceOriginSolutionId { get; private set; }
    public Guid? SourceOriginInstanceId { get; private set; }
    public Guid? SourceOriginTenantId { get; private set; }
    public Guid? SourceOriginCommunityId { get; private set; }
    public Guid? SourceOriginResourceId { get; private set; }
    public Guid? SourceOriginEventId { get; private set; }
    public Guid OccurrenceId { get; private set; }
    public Guid SourceId { get; private set; }
    public Guid SourceVersion { get; private set; }
    public Guid MaterialFactId { get; private set; }
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public Guid? ConsumedCaseId { get; private set; }
    public Guid? ConsumedOperationId { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }
    public long Revision { get; private set; }

    public ModerationEvidenceReference Evidence => ModerationEvidenceReference.Create(
        ModerationCaseTarget.Create(TargetKind, TargetId, ReadOrigin(
            TargetOriginKind, TargetOriginTrustDomainId, TargetOriginSolutionId,
            TargetOriginInstanceId, TargetOriginTenantId, TargetOriginCommunityId, TargetOriginResourceId, TargetOriginEventId), TargetVersion),
        ReadOrigin(SourceOriginKind, SourceOriginTrustDomainId,
            SourceOriginSolutionId, SourceOriginInstanceId, SourceOriginTenantId,
            SourceOriginCommunityId, SourceOriginResourceId, SourceOriginEventId),
        OccurrenceId, SourceId, SourceVersion, MaterialFactId);

    /// <summary>
    /// Retains a native issuance observation. The host selects expiry from adopted intake policy
    /// and current relationship validity, generates an unpredictable token and persists only its digest.
    /// No receipt for another purpose, guest-order token or erasure token can be substituted here.
    /// </summary>
    public static ModerationCaseTargetReceipt Issue(
        Guid id, AuthorityScope tenant, Guid callerUserId, Guid hostBindingId,
        Guid qualificationEpochId, long qualificationRevision,
        Guid relationshipSourceId, ModerationCaseRelationshipKind relationshipKind,
        ModerationCaseRelationshipGeneration relationshipGeneration,
        Guid lifetimePolicyId, long lifetimePolicyRevision,
        ModerationEvidenceReference evidence, ReadOnlySpan<byte> tokenDigest,
        DateTimeOffset issuedAt, DateTimeOffset expiresAt, string? relationshipFactsDigest = null)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(relationshipGeneration);
        ModerationHostBinding.RequireUuidV7(id, nameof(id));
        if (tenant.Kind != ModerationScopeKind.Tenant || callerUserId == Guid.Empty
            || hostBindingId == Guid.Empty || qualificationEpochId == Guid.Empty
            || qualificationRevision < 1 || relationshipSourceId == Guid.Empty
            || !Enum.IsDefined(relationshipKind) || lifetimePolicyId == Guid.Empty || lifetimePolicyRevision < 1
            || (relationshipKind is ModerationCaseRelationshipKind.EventRole or ModerationCaseRelationshipKind.CaseReadGrant)
                != (relationshipGeneration.Revision is not null)
            || (relationshipKind == ModerationCaseRelationshipKind.InstanceAdministrator)
                != (relationshipGeneration.Identity is not null)
            || (relationshipKind == ModerationCaseRelationshipKind.InstanceAdministrator
                ? relationshipFactsDigest is null || relationshipFactsDigest.Length != 64
                    || relationshipFactsDigest.Any(character => !char.IsAsciiHexDigitLower(character))
                : relationshipFactsDigest is not null)
            || relationshipKind == ModerationCaseRelationshipKind.InstanceAdministrator
                && (relationshipGeneration.Identity != relationshipSourceId
                    || evidence.Target.Kind is not (ModerationCaseTargetKind.Actor or ModerationCaseTargetKind.Service))
            || tokenDigest.Length != 32 || issuedAt == default || expiresAt <= issuedAt
            || expiresAt == DateTimeOffset.MaxValue
            || evidence.Target.Origin != tenant && evidence.Target.Origin != tenant.Parent
            || evidence.SourceOrigin != tenant && evidence.SourceOrigin != tenant.Parent)
            throw new ArgumentException("Receipt issuance requires exact native ancestry, relationship, digest and finite validity.");

        var receipt = new ModerationCaseTargetReceipt
        {
            Id = id,
            TenantId = tenant.Id,
            CallerUserId = callerUserId,
            HostBindingId = hostBindingId,
            QualificationEpochId = qualificationEpochId,
            QualificationRevision = qualificationRevision,
            RelationshipSourceId = relationshipSourceId,
            RelationshipKind = relationshipKind,
            RelationshipRevision = relationshipGeneration.Revision,
            RelationshipConcurrencyStamp = relationshipGeneration.ConcurrencyStamp,
            RelationshipFactsDigest = relationshipFactsDigest,
            LifetimePolicyId = lifetimePolicyId,
            LifetimePolicyRevision = lifetimePolicyRevision,
            TokenDigest = Convert.ToHexStringLower(tokenDigest),
            TargetKind = evidence.Target.Kind,
            TargetId = evidence.Target.Id,
            TargetVersion = evidence.Target.Version,
            OccurrenceId = evidence.OccurrenceId,
            SourceId = evidence.SourceId,
            SourceVersion = evidence.SourceVersion,
            MaterialFactId = evidence.MaterialFactId,
            IssuedAt = issuedAt.ToUniversalTime(),
            ExpiresAt = expiresAt.ToUniversalTime(),
            Revision = 1
        };
        (receipt.TargetOriginKind, receipt.TargetOriginTrustDomainId, receipt.TargetOriginSolutionId,
            receipt.TargetOriginInstanceId, receipt.TargetOriginTenantId, receipt.TargetOriginCommunityId,
            receipt.TargetOriginResourceId, receipt.TargetOriginEventId) = OriginCoordinates(evidence.Target.Origin);
        (receipt.SourceOriginKind, receipt.SourceOriginTrustDomainId, receipt.SourceOriginSolutionId,
            receipt.SourceOriginInstanceId, receipt.SourceOriginTenantId, receipt.SourceOriginCommunityId,
            receipt.SourceOriginResourceId, receipt.SourceOriginEventId) = OriginCoordinates(evidence.SourceOrigin);
        return receipt;
    }

    /// <summary>
    /// Binds one qualified case and request. The native writer must reread the actual caller,
    /// relationship, host epoch and target generations, then save this row and the case atomically.
    /// An exact retry does not allocate another occurrence or extend validity.
    /// </summary>
    public void Consume(
        Guid tenantId, Guid callerUserId, Guid hostBindingId,
        Guid qualificationEpochId, long qualificationRevision,
        Guid relationshipSourceId, ModerationCaseRelationshipGeneration relationshipGeneration,
        ModerationEvidenceReference currentEvidence, ReadOnlySpan<byte> tokenDigest,
        Guid caseId, Guid operationId, long expectedRevision, DateTimeOffset now)
    {
        ValidatePersistedState();
        if (TenantId != tenantId || CallerUserId != callerUserId || HostBindingId != hostBindingId
            || QualificationEpochId != qualificationEpochId || QualificationRevision != qualificationRevision
            || RelationshipKind == ModerationCaseRelationshipKind.CaseReadGrant
            || RelationshipSourceId != relationshipSourceId || RelationshipGeneration != relationshipGeneration
            || Evidence != currentEvidence || tokenDigest.Length != 32
            || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(TokenDigest), tokenDigest)
            || caseId == Guid.Empty || operationId == Guid.Empty || expectedRevision != Revision
            || now < IssuedAt || now >= ExpiresAt)
            throw new InvalidOperationException("The confidential intake receipt is not current and qualified.");

        if (ConsumedCaseId is not null)
        {
            if (ConsumedCaseId != caseId || ConsumedOperationId != operationId)
                throw new InvalidOperationException("The intake receipt already belongs to a different case request.");
            return;
        }

        var nextRevision = checked(Revision + 1);
        ConsumedCaseId = caseId;
        ConsumedOperationId = operationId;
        ConsumedAt = now.ToUniversalTime();
        Revision = nextRevision;
    }

    public void ValidatePersistedState()
    {
        var evidence = Evidence;
        if (Id == Guid.Empty || Id.Version != 7 || Id.Variant is < 8 or > 11
            || TenantId == Guid.Empty || CallerUserId == Guid.Empty || HostBindingId == Guid.Empty
            || QualificationEpochId == Guid.Empty || QualificationRevision < 1
            || RelationshipSourceId == Guid.Empty || !Enum.IsDefined(RelationshipKind)
            || LifetimePolicyId == Guid.Empty || LifetimePolicyRevision < 1
            || (RelationshipKind == ModerationCaseRelationshipKind.InstanceAdministrator
                ? RelationshipFactsDigest is null || RelationshipFactsDigest.Length != 64
                    || RelationshipFactsDigest.Any(character => !char.IsAsciiHexDigitLower(character))
                    || RelationshipRevision is not null
                    || RelationshipConcurrencyStamp is not null
                    || TargetKind is not (ModerationCaseTargetKind.Actor or ModerationCaseTargetKind.Service)
                : RelationshipFactsDigest is not null)
            || (RelationshipKind switch
            {
                ModerationCaseRelationshipKind.InstanceAdministrator => false,
                ModerationCaseRelationshipKind.EventRole or ModerationCaseRelationshipKind.CaseReadGrant =>
                    RelationshipRevision is null or < 1 || RelationshipConcurrencyStamp is not null,
                _ => RelationshipRevision is not null || RelationshipConcurrencyStamp is null
                    || RelationshipConcurrencyStamp == Guid.Empty
            })
            || TokenDigest.Length != 64 || TokenDigest.Any(character => !char.IsAsciiHexDigitLower(character))
            || IssuedAt == default || ExpiresAt <= IssuedAt || ExpiresAt == DateTimeOffset.MaxValue
            || RelationshipKind == ModerationCaseRelationshipKind.CaseReadGrant && Revision != 1
            || evidence.Target.Origin.Kind == ModerationScopeKind.Tenant && evidence.Target.Origin.Id != TenantId
            || evidence.SourceOrigin.Kind == ModerationScopeKind.Tenant && evidence.SourceOrigin.Id != TenantId
            || (Revision == 1
                ? ConsumedCaseId is not null || ConsumedOperationId is not null || ConsumedAt is not null
                : Revision != 2 || ConsumedCaseId is null || ConsumedCaseId == Guid.Empty
                    || ConsumedOperationId is null || ConsumedOperationId == Guid.Empty
                    || ConsumedAt is null || ConsumedAt < IssuedAt || ConsumedAt >= ExpiresAt))
            throw new ArgumentException("The persisted intake receipt provenance or consumption audit is inconsistent.");
    }

    public override string ToString() => nameof(ModerationCaseTargetReceipt);

    private static AuthorityScope ReadOrigin(
        ModerationScopeKind kind, Guid trustDomainId, Guid? solutionId, Guid? instanceId,
        Guid? tenantId, Guid? communityId, Guid? resourceId, Guid? eventId)
    {
        if (kind is not (ModerationScopeKind.Instance or ModerationScopeKind.Tenant)
            || solutionId is null || instanceId is null || communityId is not null || resourceId is not null || eventId is not null
            || (kind == ModerationScopeKind.Tenant) != (tenantId is not null))
            throw new ArgumentException("The receipt origin must retain exact instance or tenant ancestry.");
        var instance = AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(trustDomainId), solutionId.Value), instanceId.Value);
        return tenantId is { } tenant ? AuthorityScope.Tenant(instance, tenant) : instance;
    }

    private static (ModerationScopeKind, Guid, Guid?, Guid?, Guid?, Guid?, Guid?, Guid?) OriginCoordinates(AuthorityScope origin)
    {
        var instance = origin.Kind == ModerationScopeKind.Tenant ? origin.Parent! : origin;
        return (origin.Kind, origin.TrustDomainId, instance.Parent!.Id, instance.Id,
            origin.Kind == ModerationScopeKind.Tenant ? origin.Id : null, null, null, null);
    }
}
