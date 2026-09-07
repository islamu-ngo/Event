namespace Explore.Blazor.Services;

public interface ITenantRouteContextAccessor
{
    string? TenantSlug { get; }

    void SetTenantSlug(string slug);

    void Clear();

    IDisposable BeginActivityScope();
}
