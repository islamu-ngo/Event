namespace Explore.API.Hateoas.Policies;

using System.Collections.Generic;
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Hateoas;

public sealed class ReportingRoutingStateLinkPolicy : ILinkPolicy<ReportingRoutingStateDto>
{
    private const string SettingKey = "moderation-reporting";

    public IEnumerable<LinkDefinition> GetLinks(ReportingRoutingStateDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetModerationReportingRoutingState,
            null,
            "GET",
            "Moderation reporting routing state",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.TenantSettings.View,
                ResourceKinds.TenantSetting,
                TenantRoutingStateResourceId(dto),
                TenantRoutingStateScope(dto),
                TenantRoutingStateFacts(dto));

        if (!dto.TenantProviderConfigurationLocked)
        {
            yield return new LinkDefinition(
                    LinkRelations.Edit,
                    RouteNames.UpdateModerationReportingRoutingSettings,
                    Method: "PATCH",
                    RequiresAuth: true)
                .RequirePermission(AuthorizationActions.TenantSettings.Update,
                    ResourceKinds.TenantSetting,
                    TenantRoutingStateResourceId(dto),
                    TenantRoutingStateScope(dto),
                    TenantRoutingStateFacts(dto));
        }

        if (!dto.TenantProviderConfigurationLocked
            && !dto.TenantOspreyProviderLocked
            && dto.Osprey.TenantEnabled)
        {
            yield return LinkDefinition.Action(
                "test-osprey-provider",
                RouteNames.TestModerationReportingProvider,
                "POST",
                new { provider = "Osprey" })
                .RequirePermission(AuthorizationActions.TenantSettings.Update,
                    ResourceKinds.TenantSetting,
                    TenantRoutingStateResourceId(dto),
                    TenantRoutingStateScope(dto),
                    TenantRoutingStateFacts(dto));
        }

        if (!dto.TenantProviderConfigurationLocked
            && !dto.TenantCoopProviderLocked
            && dto.Coop.TenantEnabled)
        {
            yield return LinkDefinition.Action(
                "test-coop-provider",
                RouteNames.TestModerationReportingProvider,
                "POST",
                new { provider = "Coop" })
                .RequirePermission(AuthorizationActions.TenantSettings.Update,
                    ResourceKinds.TenantSetting,
                    TenantRoutingStateResourceId(dto),
                    TenantRoutingStateScope(dto),
                    TenantRoutingStateFacts(dto));
        }
    }

    private static string TenantRoutingStateResourceId(ReportingRoutingStateDto dto) =>
        $"{dto.TenantId}:{SettingKey}";

    private static IAuthorizationFacts TenantRoutingStateFacts(ReportingRoutingStateDto dto) =>
        new TenantSettingAuthorizationFacts(dto.TenantId, SettingKey, dto.TenantProviderConfigurationLocked);

    private static AuthorizationScope TenantRoutingStateScope(ReportingRoutingStateDto dto) =>
        new(TenantId: dto.TenantId.ToString());
}

public sealed class ReportingRoutingStateCollectionLinkPolicy : ICollectionLinkPolicy<ReportingRoutingStateDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(ReportingRoutingStateDto dto, ClaimsPrincipal? user) => [];

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
