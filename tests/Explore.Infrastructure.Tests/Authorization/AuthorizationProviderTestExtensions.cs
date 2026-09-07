using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Infrastructure.Tests.Authorization;

namespace Explore.Application.Contracts.Infrastructure;

internal static class AuthorizationProviderTestExtensions
{
    public static async Task<bool> IsAllowedAsync(
        this IAuthorizationProvider provider,
        string resourceKind,
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes = null,
        CancellationToken cancellationToken = default) =>
        await provider.IsAllowedWithFactsAsync(
            resourceKind,
            resourceId,
            action,
            resourceAttributes,
            cancellationToken,
            AuthorizationFactsTestFactory.Create(resourceKind, resourceId, resourceAttributes));

    public static async Task<bool> IsAllowedWithFactsAsync(
        this IAuthorizationProvider provider,
        string resourceKind,
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes,
        CancellationToken cancellationToken,
        IAuthorizationFacts? facts)
    {
        _ = resourceAttributes;

        try
        {
            var decision = await provider.AuthorizeAsync(
                new AuthorizationRequest(resourceKind, resourceId, action, Facts: facts),
                cancellationToken);
            return decision.IsAllowed;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static async Task<IReadOnlyList<bool>> IsAllowedBatchAsync(
        this IAuthorizationProvider provider,
        IReadOnlyList<AuthorizationRequest> requests,
        CancellationToken cancellationToken = default) =>
        (await provider.AuthorizeBatchAsync(requests, cancellationToken))
            .Select(decision => decision.IsAllowed)
            .ToArray();

    public static async Task<bool> CheckSettingAccessAsync(
        this IAuthorizationProvider provider,
        string settingKey,
        string action,
        Guid? tenantId = null,
        Guid? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        var (resourceKind, facts) = organizationId is { } organizationScope
            ? (ResourceKinds.Organization,
                (IAuthorizationFacts)new OrganizationAuthorizationFacts(Guid.Empty, organizationScope))
            : tenantId is { } tenantScope
                ? (ResourceKinds.TenantSetting,
                    new TenantSettingAuthorizationFacts(tenantScope))
                : (ResourceKinds.InstanceSetting, InstanceScopedAuthorizationFacts.Instance);

        var decision = await provider.AuthorizeAsync(
            new AuthorizationRequest(resourceKind, settingKey, action, Facts: facts),
            cancellationToken);
        return decision.IsAllowed;
    }
}
