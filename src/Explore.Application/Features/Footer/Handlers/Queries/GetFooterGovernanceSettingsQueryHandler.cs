using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Footer;
using Explore.Application.Features.Footer.Requests.Queries;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using MediatR;

namespace Explore.Application.Features.Footer.Handlers.Queries;

public sealed class GetFooterGovernanceSettingsQueryHandler(
    IHierarchicalSettingsResolver settingsResolver)
    : IRequestHandler<GetFooterGovernanceSettingsQuery, FooterGovernanceSettingsDto>
{
    public async Task<FooterGovernanceSettingsDto> Handle(
        GetFooterGovernanceSettingsQuery request, CancellationToken cancellationToken)
    {
        var group = await settingsResolver.ResolveGroupAsync<FooterSettingGroup>(
            new SettingContext(), cancellationToken);

        return new FooterGovernanceSettingsDto
        {
            LockTenantTemplate = group.LockTenantTemplate,
            LockTenantLinkGroups = group.LockTenantLinkGroups,
            LockTenantSocialLinks = group.LockTenantSocialLinks,
            LockTenantDescription = group.LockTenantDescription,
            LockTenantCopyright = group.LockTenantCopyright,
        };
    }
}
