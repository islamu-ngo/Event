using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Hateoas;
using Explore.Domain.Constants;

namespace Explore.API.Hateoas.Policies;

public sealed class TenantReportingIntakePolicyLinkPolicy
    : ILinkPolicy<TenantReportingIntakePolicyDto>
{
    public IEnumerable<LinkDefinition> GetLinks(
        TenantReportingIntakePolicyDto dto,
        ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
                LinkRelations.Self,
                RouteNames.GetTenantReportingIntakePolicy,
                Method: HttpMethods.Get,
                Title: "Reporting-intake policy",
                RequiresAuth: true)
            .RequirePermission(
                AuthorizationActions.TenantSettings.View,
                ResourceKinds.TenantSetting,
                GovernanceSettingKeys.EventReporting.IntakeEnabled,
                Scope(dto.TenantId),
                Facts(dto));

        if (!dto.IsLockedByInstance)
        {
            yield return LinkDefinition
                .Edit(RouteNames.UpdateTenantReportingIntakePolicy)
                .RequirePermission(
                    AuthorizationActions.TenantSettings.Update,
                    ResourceKinds.TenantSetting,
                    GovernanceSettingKeys.EventReporting.IntakeEnabled,
                    Scope(dto.TenantId),
                    Facts(dto));
        }
    }

    private static AuthorizationScope Scope(Guid tenantId) =>
        new(TenantId: tenantId.ToString());

    private static TenantSettingAuthorizationFacts Facts(TenantReportingIntakePolicyDto dto) =>
        new(
            dto.TenantId,
            GovernanceSettingKeys.EventReporting.IntakeEnabled,
            dto.IsLockedByInstance);
}

public sealed class TenantReportingIntakePolicyCollectionLinkPolicy
    : ICollectionLinkPolicy<TenantReportingIntakePolicyDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(
        TenantReportingIntakePolicyDto dto,
        ClaimsPrincipal? user) => [];

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
