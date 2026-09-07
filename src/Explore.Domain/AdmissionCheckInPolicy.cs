using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class AdmissionCheckInPolicy : ITenantEntity, IConcurrencyAware
{
    private Guid _tenantId;

    private AdmissionCheckInPolicy()
    {
    }

    private AdmissionCheckInPolicy(
        Guid id,
        AdmissionTarget target,
        DateTime opensAtUtc,
        DateTime closesAtUtc,
        int maximumEntries)
    {
        Id = id;
        TenantId = target.TenantId;
        AdmissionTargetId = target.Id;
        Target = target;
        OpensAtUtc = opensAtUtc;
        ClosesAtUtc = closesAtUtc;
        MaximumEntries = maximumEntries;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public Guid Id { get; private set; }

    public Guid TenantId
    {
        get => _tenantId;
        private set => TenantIdentity.Set(ref _tenantId, value, nameof(AdmissionCheckInPolicy));
    }

    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(AdmissionCheckInPolicy));
    }

    public Guid AdmissionTargetId { get; private set; }
    public AdmissionTarget Target { get; private set; } = null!;
    public DateTime OpensAtUtc { get; private set; }
    public DateTime ClosesAtUtc { get; private set; }
    public int MaximumEntries { get; private set; }
    public Guid ConcurrencyStamp { get; set; }

    public static AdmissionCheckInPolicy Create(
        Guid id,
        AdmissionTarget target,
        DateTime opensAtUtc,
        DateTime closesAtUtc,
        int maximumEntries)
    {
        ArgumentNullException.ThrowIfNull(target);
        RequireUuidV7(id, nameof(id));
        RequireUtc(opensAtUtc, nameof(opensAtUtc));
        RequireUtc(closesAtUtc, nameof(closesAtUtc));
        if (closesAtUtc <= opensAtUtc)
        {
            throw new ArgumentException("Admission check-in closing time must follow opening time.", nameof(closesAtUtc));
        }

        if (maximumEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEntries), "Maximum entries must be positive.");
        }

        return new AdmissionCheckInPolicy(id, target, opensAtUtc, closesAtUtc, maximumEntries);
    }

    internal bool AppliesTo(AdmissionTarget target) =>
        Target.HasSameAuthorityAs(target) &&
        TenantId == target.TenantId &&
        AdmissionTargetId == target.Id;

    private static void RequireUuidV7(Guid value, string parameterName)
    {
        if (value == Guid.Empty || value.Version != 7 || value.Variant is < 8 or > 11)
        {
            throw new ArgumentException("Admission policy identity must be an RFC 4122 UUIDv7 value.", parameterName);
        }
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Admission policy timestamps must be non-default UTC values.", parameterName);
        }
    }
}
