namespace Explore.Domain;

/// <summary>
/// Finite independent acceptance of one immutable procedure source, not applicability,
/// consent or a collection receipt. Native owners authenticate the nominated reviewer,
/// independence and qualification and fence latest policy/source/adoption generations.
/// Withdrawal preserves the original source, reviewer, writer and expiry; no renewal or
/// fallback to retired/superseded parents is possible through this lifecycle.
/// </summary>
public sealed class ModerationProfileCollectionProcedureAdoption
{
    private ModerationProfileCollectionProcedureAdoption() { }

    private ModerationProfileCollectionProcedureAdoption(Guid id, ModerationProfileCollectionProcedureSource source,
        Guid humanId, long writerGeneration, DateTimeOffset now, DateTimeOffset validUntil)
    {
        Id = id;
        Source = source;
        HostBindingId = source.HostBindingId;
        TrustDomainId = source.TrustDomainId;
        TenantId = source.TenantId;
        SourceId = source.Id;
        SourceRevision = source.Revision;
        SourceWriterGeneration = source.WriterGeneration;
        AdoptedByHumanId = humanId;
        WriterGeneration = writerGeneration;
        Revision = 1;
        AdoptedAt = now;
        ValidUntil = validUntil;
    }

    public Guid Id { get; private set; }
    public ModerationProfileCollectionProcedureSource Source { get; private set; } = null!;
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SourceId { get; private set; }
    public long SourceRevision { get; private set; }
    public long SourceWriterGeneration { get; private set; }
    public Guid AdoptedByHumanId { get; private set; }
    public long WriterGeneration { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset AdoptedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public Guid? WithdrawnByHumanId { get; private set; }
    public DateTimeOffset? WithdrawnAt { get; private set; }

    public void ValidatePersistedState()
    {
        ModerationHostBinding.RequireUuidV7(Id, nameof(Id));
        ModerationQualificationPolicySource.RequireValidity(AdoptedAt, ValidUntil);
        if (Source is null || SourceId != Source.Id || SourceRevision != 1 || SourceRevision != Source.Revision
            || SourceWriterGeneration != Source.WriterGeneration
            || SourceWriterGeneration < 1 || WriterGeneration <= SourceWriterGeneration
            || WriterGeneration == long.MaxValue || HostBindingId != Source.HostBindingId
            || TrustDomainId != Source.TrustDomainId || TenantId != Source.TenantId
            || AdoptedByHumanId != Source.ReviewerHumanId || AdoptedAt < Source.RecordedAt
            || ValidUntil > Source.ValidUntil
            || Revision is not (1 or 2)
            || Revision == 1 && (WithdrawnAt is not null || WithdrawnByHumanId is not null)
            || Revision == 2 && (WithdrawnAt is null || WithdrawnByHumanId is null
                || WithdrawnByHumanId == Guid.Empty || WithdrawnAt < AdoptedAt
                || WithdrawnAt.Value.Offset != TimeSpan.Zero || WithdrawnAt == DateTimeOffset.MaxValue))
            throw new ArgumentException("Persisted procedure acceptance requires original source coordinates and terminal withdrawal state.");
        Source.ValidatePersistedState();
    }

    public static ModerationProfileCollectionProcedureAdoption Adopt(Guid id,
        ModerationProfileCollectionProcedureSource source, ModerationChildAgePolicySource age,
        ModerationChildAgePolicyAdoption parentAdoption, Guid humanId, long writerGeneration,
        DateTimeOffset now, DateTimeOffset validUntil)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(age);
        ArgumentNullException.ThrowIfNull(parentAdoption);
        age.ValidatePersistedState();
        parentAdoption.ValidatePersistedState();
        ModerationHostBinding.RequireUuidV7(id, nameof(id));
        ModerationQualificationPolicySource.RequireValidity(now, validUntil);
        if (new[] { source.Id, age.Id, parentAdoption.Id, source.HostBindingId, source.TrustDomainId,
                source.TenantId, source.PolicyId, source.PublicationId, source.DefinitionId, source.ProfileId }.Contains(id)
            || humanId != source.ReviewerHumanId || humanId == source.RecordedByHumanId
            || humanId == source.OwnerHumanId || humanId == source.OwnerControllerHumanId
            || source.ReviewerControllerHumanId == source.OwnerControllerHumanId
            || writerGeneration <= source.WriterGeneration || writerGeneration == long.MaxValue
            || !source.MatchesCurrent(age, parentAdoption, now) || validUntil > source.ValidUntil)
            throw new ArgumentException("Procedure adoption requires the exact independent parent reviewer, newer writer and original finite source coverage.");
        var adoption = new ModerationProfileCollectionProcedureAdoption(
            id, source, humanId, writerGeneration, now, validUntil);
        adoption.ValidatePersistedState();
        return adoption;
    }

    /// <summary>
    /// Structural currentness only. Native owners must supply current parents, reject retired
    /// policy/publication/profile/mode and prove latest generations rather than echo this snapshot.
    /// </summary>
    public bool MatchesCurrent(ModerationProfileCollectionProcedureSource source, ModerationChildAgePolicySource age,
        ModerationChildAgePolicyAdoption parentAdoption, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Revision == 1 && WithdrawnAt is null && now.Offset == TimeSpan.Zero
            && now >= AdoptedAt && now < ValidUntil
            && HostBindingId == source.HostBindingId && TrustDomainId == source.TrustDomainId
            && TenantId == source.TenantId && SourceId == source.Id
            && SourceRevision == source.Revision && SourceWriterGeneration == source.WriterGeneration
            && Source.Matches(source)
            && source.MatchesCurrent(age, parentAdoption, now);
    }

    /// <summary>Accountable native withdrawal authority is authenticated outside this structural revision-CAS transition.</summary>
    public void Withdraw(long expectedRevision, Guid humanId, DateTimeOffset now)
    {
        if (Revision != expectedRevision || WithdrawnAt is not null)
            throw new InvalidOperationException("Procedure withdrawal requires the exact unwithdrawn adoption revision.");
        if (humanId == Guid.Empty || now.Offset != TimeSpan.Zero || now < AdoptedAt || now == DateTimeOffset.MaxValue)
            throw new ArgumentException("Procedure withdrawal requires accountable non-regressing native authority.");
        Revision = checked(Revision + 1);
        WithdrawnByHumanId = humanId;
        WithdrawnAt = now;
    }

    public override string ToString() => nameof(ModerationProfileCollectionProcedureAdoption);
}
