using Explore.Application.Contracts.Services;
using Microsoft.AspNetCore.Http;

namespace Explore.Infrastructure.Services;

public class TenantContextAccessor : ITenantContextAccessor
{
    public const string TenantIdItemKey = "__resolved_tenant_id";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private Guid? _tenantId;
    private Guid? _executionTenantId;

    public TenantContextAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? TenantId
    {
        get
        {
            if (_executionTenantId is { } executionTenant)
                return executionTenant;
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext?.Items.TryGetValue(TenantIdItemKey, out var value) == true && value is Guid tenantId && tenantId != Guid.Empty)
            {
                return tenantId;
            }

            return _tenantId;
        }
    }

    public bool IsResolved => TenantId.HasValue;

    public void SetTenant(Guid tenantId)
    {
        if (_executionTenantId.HasValue)
            throw new InvalidOperationException("Execution tenant facts cannot be replaced by ambient request facts.");
        if (tenantId == Guid.Empty)
        {
            return;
        }

        _tenantId = tenantId;

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            httpContext.Items[TenantIdItemKey] = tenantId;
        }
    }

    public void BindExecutionTenant(Guid tenantId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        if (_executionTenantId.HasValue)
            throw new InvalidOperationException("Execution tenant facts are single-assignment.");
        _executionTenantId = tenantId;
    }

    public void Clear()
    {
        if (_executionTenantId.HasValue)
            throw new InvalidOperationException("Execution tenant facts cannot be cleared.");
        _tenantId = null;

        var httpContext = _httpContextAccessor.HttpContext;
        httpContext?.Items.Remove(TenantIdItemKey);
    }
}
