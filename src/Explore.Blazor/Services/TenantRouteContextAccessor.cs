using Microsoft.AspNetCore.Http;

namespace Explore.Blazor.Services;

public class TenantRouteContextAccessor : ITenantRouteContextAccessor
{
    private static readonly AsyncLocal<TenantSlugHolder?> CurrentTenantSlug = new();

    public const string TenantSlugItemKey = "__tenant_slug";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private string? _tenantSlug;

    public TenantRouteContextAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? TenantSlug
    {
        get
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext?.Items.TryGetValue(TenantSlugItemKey, out var value) == true && value is string tenantSlug)
            {
                return tenantSlug;
            }

            return CurrentTenantSlug.Value?.Value ?? _tenantSlug;
        }
    }

    public void SetTenantSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return;
        }

        _tenantSlug = slug.Trim();

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            httpContext.Items[TenantSlugItemKey] = _tenantSlug;
        }
    }

    public void Clear()
    {
        _tenantSlug = null;
        CurrentTenantSlug.Value = null;

        var httpContext = _httpContextAccessor.HttpContext;
        httpContext?.Items.Remove(TenantSlugItemKey);
    }

    public IDisposable BeginActivityScope()
    {
        var previous = CurrentTenantSlug.Value;
        CurrentTenantSlug.Value = new TenantSlugHolder { Value = _tenantSlug };
        return new ActivityScope(() => CurrentTenantSlug.Value = previous);
    }

    private sealed class TenantSlugHolder
    {
        public string? Value { get; init; }
    }

    private sealed class ActivityScope(Action onDispose) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            onDispose();
        }
    }
}
