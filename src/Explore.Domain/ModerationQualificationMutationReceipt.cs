namespace Explore.Domain;

public enum ModerationQualificationSourceKind
{
    Case = 1,
    Evidence = 2,
    Reviewer = 3,
    Finding = 4,
    Policy = 5
}

/// <summary>Exact completed source mutation coordinates, never private evidence text or reusable permission.</summary>
public sealed class ModerationQualificationMutationReceipt
{
    private ModerationQualificationMutationReceipt()
    {
    }

    public Guid Id { get; private set; }
    public string RequestDigest { get; private set; } = string.Empty;
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public ModerationQualificationSourceKind SourceKind { get; private set; }
    public Guid SourceId { get; private set; }
    public Guid PrincipalId { get; private set; }
    public long ResultRevision { get; private set; }
    public long QualificationRevision { get; private set; }
    public DateTimeOffset CompletedAt { get; private set; }

    public static ModerationQualificationMutationReceipt Complete(
        Guid operationId, string requestDigest, Guid hostBindingId, Guid trustDomainId,
        Guid tenantId, ModerationQualificationSourceKind sourceKind, Guid sourceId,
        Guid principalId, long resultRevision, long qualificationRevision, DateTimeOffset completedAt)
    {
        var receipt = new ModerationQualificationMutationReceipt
        {
            Id = operationId,
            RequestDigest = requestDigest,
            HostBindingId = hostBindingId,
            TrustDomainId = trustDomainId,
            TenantId = tenantId,
            SourceKind = sourceKind,
            SourceId = sourceId,
            PrincipalId = principalId,
            ResultRevision = resultRevision,
            QualificationRevision = qualificationRevision,
            CompletedAt = completedAt.ToUniversalTime()
        };
        receipt.ValidatePersistedState();
        return receipt;
    }

    public void ValidatePersistedState()
    {
        ModerationHostBinding.RequireUuidV7(Id, nameof(Id));
        ModerationHostBinding.RequireUuidV7(HostBindingId, nameof(HostBindingId));
        ModerationHostBinding.RequireUuidV7(TrustDomainId, nameof(TrustDomainId));
        ModerationHostBinding.RequireUuidV7(TenantId, nameof(TenantId));
        ModerationHostBinding.RequireUuidV7(SourceId, nameof(SourceId));
        if (PrincipalId == Guid.Empty || !Enum.IsDefined(SourceKind)
            || string.IsNullOrWhiteSpace(RequestDigest) || RequestDigest.Length != 64
            || !RequestDigest.All(char.IsAsciiHexDigit)
            || ResultRevision < 1 || QualificationRevision < 1
            || CompletedAt == DateTimeOffset.MinValue || CompletedAt == DateTimeOffset.MaxValue
            || CompletedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("A receipt requires the exact qualified source outcome and completion instant.");
        }
    }

    public override string ToString() => nameof(ModerationQualificationMutationReceipt);
}
