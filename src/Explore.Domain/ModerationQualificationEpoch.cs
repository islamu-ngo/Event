namespace Explore.Domain;

/// <summary>
/// A positive qualification revision for one host binding and one actual tenant jurisdiction.
/// Initialization and advancement confer neither permission nor publication readiness.
/// Persistence must enforce unique binding/tenant ownership and atomic expected-revision writes.
/// </summary>
public sealed class ModerationQualificationEpoch
{
    private ModerationQualificationEpoch()
    {
    }

    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TenantId { get; private set; }
    public long Revision { get; private set; }

    /// <summary>
    /// Explicitly starts revision one using an installation-allocated epoch identity and a resolved
    /// native tenant. The host must establish that the tenant exists; no synthetic tenant is created.
    /// </summary>
    public static ModerationQualificationEpoch Initialize(
        Guid id,
        ModerationHostBinding binding,
        Tenant tenant)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(tenant);
        ModerationHostBinding.RequireUuidV7(id, nameof(id));
        ModerationHostBinding.RequireUuidV7(binding.Id, nameof(binding));
        ModerationHostBinding.RequireUuidV7(tenant.Id, nameof(tenant));

        return new ModerationQualificationEpoch
        {
            Id = id,
            HostBindingId = binding.Id,
            TenantId = tenant.Id,
            Revision = 1
        };
    }

    /// <summary>
    /// Advances only the exact positive expected revision. Stale attempts and checked overflow
    /// fail before mutation; this in-memory check does not replace transactional concurrency control.
    /// </summary>
    public void Advance(long expectedRevision)
    {
        if (Revision <= 0 || expectedRevision != Revision)
        {
            throw new InvalidOperationException("Qualification advancement requires the exact positive current revision.");
        }

        long nextRevision = checked(Revision + 1);
        Revision = nextRevision;
    }
}
