using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Footer;
using Explore.Application.Features.Footer.Requests.Queries;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using MediatR;

namespace Explore.Application.Features.Footer.Handlers.Queries;

public sealed class GetFooterConfigQueryHandler(
    IHierarchicalSettingsResolver settingsResolver,
    IFooterLinkGroupRepository footerLinkGroupRepository,
    ITenantContext tenantContext,
    IMapper mapper)
    : IRequestHandler<GetFooterConfigQuery, FooterConfigDto>
{
    public async Task<FooterConfigDto> Handle(
        GetFooterConfigQuery request, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId;

        var settingGroupTask = settingsResolver.ResolveGroupAsync<FooterSettingGroup>(
            new SettingContext(TenantId: tenantId), cancellationToken);
        var groupsTask = footerLinkGroupRepository.GetResolvedGroupsForTenantAsync(tenantId, cancellationToken);

        await Task.WhenAll(settingGroupTask, groupsTask);

        var settingGroup = settingGroupTask.Result;
        var groups = groupsTask.Result;

        var settings = new FooterSettingsDto
        {
            Enabled = settingGroup.Enabled,
            Template = settingGroup.Template,
            ShowDescription = settingGroup.ShowDescription,
            DescriptionText = settingGroup.DescriptionText,
            ShowSocialLinks = settingGroup.ShowSocialLinks,
            SocialLinks = settingGroup.SocialLinks.AsReadOnly(),
            CopyrightText = settingGroup.CopyrightText,
            ShowCookieSettingsLink = settingGroup.ShowCookieSettingsLink,
        };

        return new FooterConfigDto
        {
            Settings = settings,
            LinkGroups = mapper.Map<List<FooterLinkGroupDto>>(groups),
        };
    }
}
