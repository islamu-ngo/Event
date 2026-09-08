using Explore.Application.Contracts.Services;
using Microsoft.AspNetCore.Http;

namespace Explore.Infrastructure.Services;

public class TenantContextAccessor : ITenantContextAccessor
{
    public const string TenantIdItemKey = "__resolved_tenant_id";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private Guid? _tenantId;

    public TenantContextAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? TenantId
    {
        get
        {
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

    public void Clear()
    {
        _tenantId = null;

        var httpContext = _httpContextAccessor.HttpContext;
        httpContext?.Items.Remove(TenantIdItemKey);
    }
}
