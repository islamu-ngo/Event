namespace Explore.Application.Contracts.Services;

public interface ITenantContextAccessor
{
    Guid? TenantId { get; }

    bool IsResolved { get; }

    void SetTenant(Guid tenantId);

    /// <summary>Binds immutable child-execution facts without changing the ambient HTTP request.</summary>
    void BindExecutionTenant(Guid tenantId);

    void Clear();
}
