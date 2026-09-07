using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.PlatformMonetization;
using Explore.Application.Features.PlatformMonetization.Requests.Queries;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class PlatformMonetizationSettingsLinkPolicy : ILinkPolicy<PlatformMonetizationSettingsDto>
{
    public IEnumerable<LinkDefinition> GetLinks(PlatformMonetizationSettingsDto dto, ClaimsPrincipal? user)
    {
        yield return LinkDefinition.Self(RouteNames.GetInstancePlatformMonetizationSettings)
            .RequirePermission(AuthorizationActions.InstanceSettings.View,
                ResourceKinds.InstanceSetting,
                GetPlatformMonetizationSettingsQuery.SettingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);

        yield return new LinkDefinition(
                LinkRelations.Edit,
                RouteNames.UpdateInstancePlatformMonetizationSettings,
                null,
                HttpMethods.Put,
                RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.Update,
                ResourceKinds.InstanceSetting,
                GetPlatformMonetizationSettingsQuery.SettingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);
    }

}

/// <summary>Monetization settings are a singleton resource; the collection shape has no affordances.</summary>
public sealed class PlatformMonetizationSettingsCollectionLinkPolicy : ICollectionLinkPolicy<PlatformMonetizationSettingsDto>;
