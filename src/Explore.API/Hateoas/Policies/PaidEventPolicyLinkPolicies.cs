using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.PaidEventPolicies;
using Explore.Application.Features.PaidEventPolicies.Requests.Queries;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class InstancePaidEventPolicyLinkPolicy : ILinkPolicy<PaidEventPolicyDto>
{
    public IEnumerable<LinkDefinition> GetLinks(PaidEventPolicyDto dto, ClaimsPrincipal? user)
    {
        yield return LinkDefinition.Self(RouteNames.GetInstancePaidEventPolicySettings)
            .RequirePermission(AuthorizationActions.InstanceSettings.View,
                ResourceKinds.InstanceSetting,
                GetInstancePaidEventPolicyQuery.SettingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);

        yield return LinkDefinition.Edit(RouteNames.UpdateInstancePaidEventPolicySettings)
            .RequirePermission(AuthorizationActions.InstanceSettings.Update,
                ResourceKinds.InstanceSetting,
                GetInstancePaidEventPolicyQuery.SettingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);
    }

}

public sealed class InstancePaidEventPolicyCollectionLinkPolicy : ICollectionLinkPolicy<PaidEventPolicyDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(PaidEventPolicyDto dto, ClaimsPrincipal? user) => [];

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}

public sealed class TenantPaidEventPolicyConfigurationLinkPolicy : ILinkPolicy<TenantPaidEventPolicyConfigurationDto>
{
    public IEnumerable<LinkDefinition> GetLinks(TenantPaidEventPolicyConfigurationDto dto, ClaimsPrincipal? user)
    {
        yield return LinkDefinition.Self(RouteNames.GetTenantPaidEventPolicySettings, new { tenantId = dto.TenantId })
            .RequirePermission(AuthorizationActions.TenantSettings.View,
                ResourceKinds.TenantSetting,
                ResourceId(dto.TenantId),
                Scope(dto.TenantId),
                new TenantSettingAuthorizationFacts(dto.TenantId));

        yield return LinkDefinition.Edit(RouteNames.UpdateTenantPaidEventPolicySettings, new { tenantId = dto.TenantId })
            .RequirePermission(AuthorizationActions.TenantSettings.Update,
                ResourceKinds.TenantSetting,
                ResourceId(dto.TenantId),
                Scope(dto.TenantId),
                new TenantSettingAuthorizationFacts(dto.TenantId));
    }

    private static string ResourceId(Guid tenantId) => $"{tenantId}:paid-event-policy";

    private static AuthorizationScope Scope(Guid tenantId) => new(TenantId: tenantId.ToString());
}

public sealed class TenantPaidEventPolicyConfigurationCollectionLinkPolicy : ICollectionLinkPolicy<TenantPaidEventPolicyConfigurationDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(TenantPaidEventPolicyConfigurationDto dto, ClaimsPrincipal? user) => [];

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
