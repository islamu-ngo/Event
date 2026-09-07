namespace Explore.Application.Features.Tenants;

public static class TenantMutationLockKeys
{
    private const string SlugPrefix = "tenant.slug.";

    public static string ForSlug(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        return $"{SlugPrefix}{slug.Trim().ToLowerInvariant()}";
    }
}
