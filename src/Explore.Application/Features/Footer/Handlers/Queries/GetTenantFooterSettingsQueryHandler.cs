using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Footer;
using Explore.Application.Features.Footer.Requests.Queries;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using MediatR;

namespace Explore.Application.Features.Footer.Handlers.Queries;

public sealed class GetTenantFooterSettingsQueryHandler(
    IHierarchicalSettingsResolver settingsResolver,
    ITenantContext tenantContext,
    IDeploymentModeProvider deploymentModeProvider)
    : IRequestHandler<GetTenantFooterSettingsQuery, TenantFooterSettingsDto>
{
    public async Task<TenantFooterSettingsDto> Handle(
        GetTenantFooterSettingsQuery request,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId;
        var settings = await settingsResolver.ResolveGroupAsync<FooterSettingGroup>(
            new SettingContext(TenantId: tenantId),
            cancellationToken);
        var isSingleTenant = await deploymentModeProvider.IsSingleTenantAsync(cancellationToken);

        return new TenantFooterSettingsDto
        {
            TenantId = tenantId,
            Enabled = settings.Enabled,
            Template = settings.Template,
            ShowDescription = settings.ShowDescription,
            DescriptionText = settings.DescriptionText,
            ShowSocialLinks = settings.ShowSocialLinks,
            SocialLinks = settings.SocialLinks.AsReadOnly(),
            CopyrightText = settings.CopyrightText,
            ShowCookieSettingsLink = settings.ShowCookieSettingsLink,
            LockTenantTemplate = settings.LockTenantTemplate,
            LockTenantDescription = settings.LockTenantDescription,
            LockTenantLinkGroups = !isSingleTenant && settings.LockTenantLinkGroups,
            LockTenantSocialLinks = settings.LockTenantSocialLinks,
            LockTenantCopyright = settings.LockTenantCopyright
        };
    }
}
