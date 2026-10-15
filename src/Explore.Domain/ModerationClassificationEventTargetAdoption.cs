namespace Explore.Domain;

/// <summary>
/// Independent adoption of one exact accepted classification Content/Event partition.
/// Terminal revocation needs no live Event and grants no replacement authority.
/// </summary>
public sealed class ModerationClassificationEventTargetAdoption
{
    private ModerationClassificationEventTargetAdoption() { }
    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SelectionId { get; private set; }
    public long SelectionRevision { get; private set; }
    public long SelectionWriterGeneration { get; private set; }
    public int BindingOrdinal { get; private set; }
    public Guid GenerationId { get; private set; }
    public long GenerationRevision { get; private set; }
    public long GenerationWriterGeneration { get; private set; }
    public Guid EventId { get; private set; }
    public Guid EventStamp { get; private set; }
    public Guid OwnerHumanId { get; private set; }
    public Guid OwnerControllerHumanId { get; private set; }
    public Guid ReviewerHumanId { get; private set; }
    public Guid ReviewerControllerHumanId { get; private set; }
    public Guid SelectionAuthoredByHumanId { get; private set; }
    public Guid AdoptedByHumanId { get; private set; }
    public long WriterGeneration { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset SelectionRecordedAt { get; private set; }
    public DateTimeOffset SelectionValidUntil { get; private set; }
    public DateTimeOffset GenerationObservedAt { get; private set; }
    public DateTimeOffset GenerationValidUntil { get; private set; }
    public DateTimeOffset AdoptedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public Guid? RevokedByHumanId { get; private set; }
    public long? RevocationWriterGeneration { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevocationGrounds { get; private set; }

    public static ModerationClassificationEventTargetAdoption Adopt(
        Guid id, ModerationClassificationControlSelection selection, int bindingOrdinal,
        ModerationEventGeneration generation, Guid humanId, long writerGeneration,
        DateTimeOffset now, DateTimeOffset validUntil)
    {
        selection.ValidatePersistedState();
        generation.ValidatePersistedState();
        if (!selection.Bindings.Any(binding => binding.Ordinal == bindingOrdinal)
            || selection.IsConflictingHuman(humanId)
            || selection.GenerationId != generation.Id || selection.GenerationRevision != generation.Revision
            || selection.GenerationWriterGeneration != generation.WriterGeneration || selection.EventId != generation.EventId
            || selection.EventStamp != generation.EventStamp || selection.HostBindingId != generation.HostBindingId
            || selection.TrustDomainId != generation.TrustDomainId || selection.TenantId != generation.TenantId
            || selection.GenerationObservedAt != generation.ObservedAt || selection.GenerationValidUntil != generation.ValidUntil)
            throw new ArgumentException("Adoption requires an independent human and the exact selected Content/Event generation and ordinal.");
        var row = new ModerationClassificationEventTargetAdoption
        {
            Id = id, HostBindingId = selection.HostBindingId, TrustDomainId = selection.TrustDomainId, TenantId = selection.TenantId,
            SelectionId = selection.Id, SelectionRevision = selection.Revision, SelectionWriterGeneration = selection.WriterGeneration,
            BindingOrdinal = bindingOrdinal, GenerationId = generation.Id, GenerationRevision = generation.Revision,
            GenerationWriterGeneration = generation.WriterGeneration, EventId = generation.EventId, EventStamp = generation.EventStamp,
            OwnerHumanId = selection.OwnerHumanId, OwnerControllerHumanId = selection.OwnerControllerHumanId,
            ReviewerHumanId = selection.ReviewerHumanId, ReviewerControllerHumanId = selection.ReviewerControllerHumanId,
            SelectionAuthoredByHumanId = selection.AuthoredByHumanId, AdoptedByHumanId = humanId,
            WriterGeneration = writerGeneration, Revision = 1, SelectionRecordedAt = selection.RecordedAt,
            SelectionValidUntil = selection.ValidUntil, GenerationObservedAt = generation.ObservedAt,
            GenerationValidUntil = generation.ValidUntil, AdoptedAt = now, ValidUntil = validUntil
        };
        row.ValidatePersistedState();
        return row;
    }

    /// <summary>Requires the current independent material review in addition to this retained adoption.</summary>
    public bool MatchesCurrent(ModerationClassificationControlSelection selection,
        ModerationClassificationMaterialSource source, ModerationClassificationMaterialReview review,
        ModerationEventGeneration generation, DateTimeOffset now) =>
        Revision == 1 && RevokedAt is null && now.Offset == TimeSpan.Zero && now >= AdoptedAt && now < ValidUntil
        && SelectionId == selection.Id && SelectionRevision == selection.Revision
        && SelectionWriterGeneration == selection.WriterGeneration && SelectionRecordedAt == selection.RecordedAt
        && SelectionValidUntil == selection.ValidUntil && selection.Bindings.Any(binding => binding.Ordinal == BindingOrdinal)
        && HostBindingId == selection.HostBindingId && TrustDomainId == selection.TrustDomainId && TenantId == selection.TenantId
        && OwnerHumanId == selection.OwnerHumanId && OwnerControllerHumanId == selection.OwnerControllerHumanId
        && ReviewerHumanId == selection.ReviewerHumanId && ReviewerControllerHumanId == selection.ReviewerControllerHumanId
        && SelectionAuthoredByHumanId == selection.AuthoredByHumanId && !selection.IsConflictingHuman(AdoptedByHumanId)
        && GenerationId == generation.Id && GenerationRevision == generation.Revision
        && GenerationWriterGeneration == generation.WriterGeneration && EventId == generation.EventId && EventStamp == generation.EventStamp
        && GenerationObservedAt == generation.ObservedAt && GenerationValidUntil == generation.ValidUntil
        && selection.MatchesCurrent(source, review, generation, now);

    public void Revoke(long expectedRevision, Guid humanId, long writerGeneration, string grounds, DateTimeOffset now)
    {
        ValidatePersistedState();
        if (expectedRevision != Revision || RevokedAt is not null)
            throw new InvalidOperationException("Revocation requires the exact unrevoked classification adoption.");
        if (humanId == Guid.Empty || writerGeneration <= WriterGeneration || writerGeneration == long.MaxValue
            || !ModerationChildAgePolicyTerms.Grounds(grounds) || now.Offset != TimeSpan.Zero
            || now < AdoptedAt || now == DateTimeOffset.MaxValue)
            throw new ArgumentException("Revocation requires accountable current authority, substantive grounds and non-regressing time.");
        Revision = 2;
        RevokedByHumanId = humanId;
        RevocationWriterGeneration = writerGeneration;
        RevocationGrounds = grounds;
        RevokedAt = now;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, HostBindingId, TrustDomainId, TenantId, SelectionId, GenerationId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        ModerationQualificationPolicySource.RequireValidity(SelectionRecordedAt, SelectionValidUntil);
        ModerationQualificationPolicySource.RequireValidity(GenerationObservedAt, GenerationValidUntil);
        ModerationQualificationPolicySource.RequireValidity(AdoptedAt, ValidUntil);
        if (Id == SelectionId || Id == GenerationId || SelectionId == GenerationId
            || SelectionRevision != 1 || SelectionWriterGeneration is < 1 or long.MaxValue
            || GenerationRevision is < 1 or long.MaxValue || GenerationWriterGeneration is < 1 or long.MaxValue
            || BindingOrdinal is < 0 or >= 64 || EventId == Guid.Empty || EventStamp == Guid.Empty
            || OwnerHumanId == Guid.Empty || OwnerControllerHumanId == Guid.Empty
            || ReviewerHumanId == Guid.Empty || ReviewerControllerHumanId == Guid.Empty
            || SelectionAuthoredByHumanId == Guid.Empty || AdoptedByHumanId == Guid.Empty
            || AdoptedByHumanId == OwnerHumanId || AdoptedByHumanId == OwnerControllerHumanId
            || AdoptedByHumanId == ReviewerHumanId || AdoptedByHumanId == ReviewerControllerHumanId
            || AdoptedByHumanId == SelectionAuthoredByHumanId
            || WriterGeneration <= Math.Max(SelectionWriterGeneration, GenerationWriterGeneration) || WriterGeneration == long.MaxValue
            || AdoptedAt < SelectionRecordedAt || AdoptedAt < GenerationObservedAt
            || ValidUntil > SelectionValidUntil || ValidUntil > GenerationValidUntil
            || !(Revision == 1 && RevokedByHumanId is null && RevocationWriterGeneration is null
                    && RevokedAt is null && RevocationGrounds is null
                || Revision == 2 && RevokedByHumanId is { } human && human != Guid.Empty
                    && RevocationWriterGeneration > WriterGeneration && RevocationWriterGeneration != long.MaxValue
                    && RevokedAt is { } at && at.Offset == TimeSpan.Zero && at >= AdoptedAt && at != DateTimeOffset.MaxValue
                    && ModerationChildAgePolicyTerms.Grounds(RevocationGrounds!)))
            throw new ArgumentException("Classification adoption requires immutable finite birth and consistent accountable terminal revocation.");
    }

    public override string ToString() => nameof(ModerationClassificationEventTargetAdoption);
}
