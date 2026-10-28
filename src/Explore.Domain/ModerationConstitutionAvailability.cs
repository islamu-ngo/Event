namespace Explore.Domain;

/// <summary>
/// A reviewer's own bounded workload/exposure choice. It supplies neither identity qualification
/// nor case assignment, and cannot be renewed by modifying an earlier generation.
/// </summary>
public sealed class ModerationConstitutionAvailability
{
    private ModerationConstitutionAvailability() { }
    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ReviewerHumanId { get; private set; }
    public int MaximumCaseload { get; private set; }
    public long WriterGeneration { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public Guid? WithdrawnByHumanId { get; private set; }
    public DateTimeOffset? WithdrawnAt { get; private set; }

    public static ModerationConstitutionAvailability Record(Guid id, Guid hostId, Guid trustId, Guid tenantId,
        Guid reviewerHumanId, long writerGeneration, int maximumCaseload, DateTimeOffset now, DateTimeOffset validUntil)
    {
        var row = new ModerationConstitutionAvailability
        {
            Id = id,
            HostBindingId = hostId,
            TrustDomainId = trustId,
            TenantId = tenantId,
            ReviewerHumanId = reviewerHumanId,
            MaximumCaseload = maximumCaseload,
            WriterGeneration = writerGeneration,
            Revision = 1,
            RecordedAt = now,
            ValidUntil = validUntil
        };
        row.ValidatePersistedState();
        return row;
    }
    public bool IsCurrent(DateTimeOffset now) => Revision == 1 && WithdrawnAt is null && now >= RecordedAt && now < ValidUntil;
    public void Withdraw(long expectedRevision, Guid humanId, DateTimeOffset now)
    {
        ValidatePersistedState();
        if (expectedRevision != Revision || WithdrawnAt is not null)
            throw new InvalidOperationException("Reviewer availability withdrawal is terminal.");
        if (humanId == Guid.Empty || now < RecordedAt || now.Offset != TimeSpan.Zero || now == DateTimeOffset.MaxValue)
            throw new ArgumentException("Withdrawal requires accountable non-regressing authority.");
        Revision = checked(Revision + 1);
        WithdrawnAt = now;
        WithdrawnByHumanId = humanId;
    }
    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, HostBindingId, TrustDomainId, TenantId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        if (ReviewerHumanId == Guid.Empty || WriterGeneration is < 1 or long.MaxValue || MaximumCaseload < 1)
            throw new ArgumentException("Availability requires a native human, finite generation and explicit positive caseload.");
        ModerationQualificationPolicySource.RequireValidity(RecordedAt, ValidUntil);
        if (!(Revision == 1 && WithdrawnAt is null && WithdrawnByHumanId is null
            || Revision == 2 && WithdrawnAt is { } withdrawn && WithdrawnByHumanId is { } human && human != Guid.Empty
                && withdrawn >= RecordedAt && withdrawn.Offset == TimeSpan.Zero && withdrawn != DateTimeOffset.MaxValue))
            throw new ArgumentException("Reviewer availability withdrawal state is inconsistent.");
    }
    public override string ToString() => nameof(ModerationConstitutionAvailability);
}
