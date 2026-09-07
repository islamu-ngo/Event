namespace Explore.Application.Contracts.Services;

public interface ITenantContextAccessor
{
    Guid? TenantId { get; }

    bool IsResolved { get; }

    void SetTenant(Guid tenantId);

    void Clear();
}
