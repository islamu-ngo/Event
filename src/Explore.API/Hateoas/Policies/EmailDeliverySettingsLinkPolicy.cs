
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Settings;
using Explore.Application.Hateoas;
using Explore.Domain.Constants;

namespace Explore.API.Hateoas.Policies;

public sealed class EmailDeliverySettingsLinkPolicy : ILinkPolicy<InstanceSmtpSettingsDto>
{
    public IEnumerable<LinkDefinition> GetLinks(InstanceSmtpSettingsDto dto, ClaimsPrincipal? user)
    {
        yield return new(LinkRelations.Self, RouteNames.GetInstanceSmtpSettings, null, "GET", RequiresAuth: true);
        if (!dto.CanManageDelivery) yield break;
        yield return Authorized(LinkRelations.Edit, RouteNames.UpdateInstanceSmtpSettings, "PATCH", null);
        if (dto.DeliveryEnabled)
            yield return Authorized("disable-preview", RouteNames.PreviewInstanceSmtpDisable, "POST", null);
    }

    public static IEnumerable<LinkDefinition> TenantLinks(SettingGroupResponseDto dto)
    {
        yield return new(LinkRelations.Self, RouteNames.GetTenantScopedSettings,
            new { category = dto.Category }, "GET", RequiresAuth: true);
        if (dto.Category != "Email") yield break;
        var enabled = dto.Settings.SingleOrDefault(setting => setting.Key == GovernanceSettingKeys.Email.DeliveryEnabled);
        if (enabled is not { CanEdit: true }) yield break;
        if (bool.TryParse(enabled.Value, out bool isEnabled) && isEnabled)
            yield return Authorized("disable-preview", RouteNames.PreviewTenantSmtpDisable, "POST", dto.TenantId);
        else
            yield return Authorized("enable", RouteNames.UpdateTenantSetting, "PUT", dto.TenantId,
                new { key = GovernanceSettingKeys.Email.DeliveryEnabled });
    }

    internal static LinkDefinition Authorized(string relation, string route, string method, Guid? tenantId, object? values = null)
    {
        var link = new LinkDefinition(relation, route, values, method, RequiresAuth: true);
        return tenantId is { } tenant
            ? link.RequirePermission(AuthorizationActions.Update, ResourceKinds.Tenant, tenant.ToString("D"),
                new AuthorizationScope(TenantId: tenant.ToString("D")), new TenantScopedAuthorizationFacts(tenant))
            : link.RequirePermission(AuthorizationActions.InstanceSettings.Update, ResourceKinds.InstanceSetting,
                GovernanceSettingKeys.Email.DeliveryEnabled, facts: InstanceScopedAuthorizationFacts.Instance);
    }

}

public sealed class EmailDeliverySettingsCollectionLinkPolicy : ICollectionLinkPolicy<InstanceSmtpSettingsDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(InstanceSmtpSettingsDto dto, ClaimsPrincipal? user) => [];
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}

public sealed class EmailDeliveryDisablePreviewLinkPolicy : ILinkPolicy<EmailDeliveryDisablePreviewDto>
{
    public IEnumerable<LinkDefinition> GetLinks(EmailDeliveryDisablePreviewDto dto, ClaimsPrincipal? user)
    {
        yield return EmailDeliverySettingsLinkPolicy.Authorized("settings", dto.TenantId.HasValue
            ? RouteNames.GetTenantScopedSettings : RouteNames.GetInstanceSmtpSettings, "GET", dto.TenantId,
            dto.TenantId.HasValue ? new { category = "Email" } : null);
        yield return EmailDeliverySettingsLinkPolicy.Authorized("disable-preview", dto.TenantId.HasValue
            ? RouteNames.PreviewTenantSmtpDisable : RouteNames.PreviewInstanceSmtpDisable, "POST", dto.TenantId);
        if (dto.CanDisable && dto.ConfirmationToken is not null)
            yield return EmailDeliverySettingsLinkPolicy.Authorized("disable", dto.TenantId.HasValue
                ? RouteNames.DisableTenantSmtp : RouteNames.DisableInstanceSmtp, "POST", dto.TenantId);
    }
}

public sealed class EmailDeliveryDisablePreviewCollectionLinkPolicy : ICollectionLinkPolicy<EmailDeliveryDisablePreviewDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(EmailDeliveryDisablePreviewDto dto, ClaimsPrincipal? user) => [];
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
