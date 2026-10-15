using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

/// <summary>Finite independent adoption of one exact draft partition and native observed generation.</summary>
public sealed class ModerationResourceTargetAdoption
{
    private ModerationResourceTargetAdoption() { }
    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SelectionId { get; private set; }
    public long SelectionRevision { get; private set; }
    public int BindingOrdinal { get; private set; }
    public Guid GenerationId { get; private set; }
    public long GenerationRevision { get; private set; }
    public long GenerationWriterGeneration { get; private set; }
    public Guid CaseId { get; private set; }
    public long CaseRevision { get; private set; }
    public Guid AdoptedByHumanId { get; private set; }
    public long WriterGeneration { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset AdoptedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public Guid? RevokedByHumanId { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static ModerationResourceTargetAdoption Adopt(
        Guid id, ModerationContributionSelection selection, ModerationContributionBinding binding,
        ModerationResourceGeneration generation, Guid humanId, long writerGeneration,
        DateTimeOffset now, DateTimeOffset validUntil)
    {
        selection.ValidatePersistedState();
        binding.ValidatePersistedState();
        generation.ValidatePersistedState();
        if (humanId == selection.AuthoredByHumanId || binding.SelectionId != selection.Id
            || !selection.Bindings.Contains(binding) || generation.HostBindingId != selection.HostBindingId
            || generation.TrustDomainId != selection.TrustDomainId || generation.TenantId != selection.TenantId
            || generation.ResourceId != binding.TargetId || generation.Revision != binding.TargetRevision
            || binding.ScopeKind is not (ModerationScopeKind.Tenant or ModerationScopeKind.Resource)
            || binding.ScopeTenantId != generation.TenantId
            || binding.ScopeKind == ModerationScopeKind.Resource && binding.ScopeResourceId != generation.ResourceId
            || now < selection.RecordedAt || now < generation.ObservedAt
            || validUntil > selection.ValidUntil || validUntil > generation.ValidUntil)
            throw new ArgumentException("Target adoption requires independent approval of the exact retained resource partition and generation.");
        var row = new ModerationResourceTargetAdoption
        {
            Id = id, HostBindingId = selection.HostBindingId, TrustDomainId = selection.TrustDomainId,
            TenantId = selection.TenantId, SelectionId = selection.Id, SelectionRevision = selection.Revision,
            BindingOrdinal = binding.Ordinal, GenerationId = generation.Id, GenerationRevision = generation.Revision,
            GenerationWriterGeneration = generation.WriterGeneration,
            CaseId = selection.CaseId, CaseRevision = selection.CaseRevision, AdoptedByHumanId = humanId,
            WriterGeneration = writerGeneration, Revision = 1, AdoptedAt = now.ToUniversalTime(), ValidUntil = validUntil
        };
        row.ValidatePersistedState();
        return row;
    }

    public void Revoke(long expectedRevision, Guid humanId, DateTimeOffset now)
    {
        ValidatePersistedState();
        if (expectedRevision != Revision || RevokedAt is not null)
            throw new InvalidOperationException("Target revocation requires its exact current adoption.");
        if (humanId == Guid.Empty || now < AdoptedAt || now == DateTimeOffset.MaxValue)
            throw new ArgumentException("Target revocation requires current native administration and non-regressing time.");
        Revision = checked(Revision + 1);
        RevokedByHumanId = humanId;
        RevokedAt = now.ToUniversalTime();
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, HostBindingId, TrustDomainId, TenantId, SelectionId, GenerationId, CaseId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        if (SelectionRevision != 1 || GenerationRevision is < 1 or long.MaxValue
            || GenerationWriterGeneration is < 1 or long.MaxValue || BindingOrdinal is < 0 or >= 64
            || CaseRevision is < 1 or long.MaxValue || AdoptedByHumanId == Guid.Empty
            || WriterGeneration is < 1 or long.MaxValue || AdoptedAt.Offset != TimeSpan.Zero
            || AdoptedAt == DateTimeOffset.MinValue || ValidUntil.Offset != TimeSpan.Zero
            || ValidUntil == DateTimeOffset.MaxValue || ValidUntil <= AdoptedAt
            || (Revision == 1
                ? RevokedAt is not null || RevokedByHumanId is not null
                : Revision != 2 || RevokedAt is null || RevokedByHumanId is null || RevokedByHumanId == Guid.Empty
                    || RevokedAt < AdoptedAt || RevokedAt == DateTimeOffset.MaxValue
                    || RevokedAt.Value.Offset != TimeSpan.Zero))
            throw new ArgumentException("A target adoption requires exact finite coordinates and accountable terminal revocation.");
    }

    public override string ToString() => nameof(ModerationResourceTargetAdoption);
}
