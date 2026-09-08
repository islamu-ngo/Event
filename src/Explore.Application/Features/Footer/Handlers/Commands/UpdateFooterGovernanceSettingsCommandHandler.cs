using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Footer.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using MediatR;

namespace Explore.Application.Features.Footer.Handlers.Commands;

public sealed class UpdateFooterGovernanceSettingsCommandHandler(
    IAdminContext adminContext,
    IHierarchicalSettingsResolver settingsResolver)
    : IRequestHandler<UpdateFooterGovernanceSettingsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        UpdateFooterGovernanceSettingsCommand request, CancellationToken cancellationToken)
    {
        if (!await adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken))
            return BaseCommandResponse.Authorization<Guid>(
                "Only instance administrators can update footer governance settings.");

        if (!request.Patch.HasChanges())
            return BaseCommandResponse.Validation<Guid>(
                ["At least one footer governance setting must be provided."],
                "At least one footer governance setting must be provided.");

        if (request.Patch.LockTenantTemplate.HasValue)
            await settingsResolver.SetValueAsync(
                GovernanceSettingKeys.Footer.LockTenantTemplate,
                SettingValueSerializer.Serialize(request.Patch.LockTenantTemplate.Value),
                SettingScope.Instance, Guid.Empty, request.UserId, cancellationToken);

        if (request.Patch.LockTenantLinkGroups.HasValue)
            await settingsResolver.SetValueAsync(
                GovernanceSettingKeys.Footer.LockTenantLinkGroups,
                SettingValueSerializer.Serialize(request.Patch.LockTenantLinkGroups.Value),
                SettingScope.Instance, Guid.Empty, request.UserId, cancellationToken);

        if (request.Patch.LockTenantSocialLinks.HasValue)
            await settingsResolver.SetValueAsync(
                GovernanceSettingKeys.Footer.LockTenantSocialLinks,
                SettingValueSerializer.Serialize(request.Patch.LockTenantSocialLinks.Value),
                SettingScope.Instance, Guid.Empty, request.UserId, cancellationToken);

        if (request.Patch.LockTenantDescription.HasValue)
            await settingsResolver.SetValueAsync(
                GovernanceSettingKeys.Footer.LockTenantDescription,
                SettingValueSerializer.Serialize(request.Patch.LockTenantDescription.Value),
                SettingScope.Instance, Guid.Empty, request.UserId, cancellationToken);

        if (request.Patch.LockTenantCopyright.HasValue)
            await settingsResolver.SetValueAsync(
                GovernanceSettingKeys.Footer.LockTenantCopyright,
                SettingValueSerializer.Serialize(request.Patch.LockTenantCopyright.Value),
                SettingScope.Instance, Guid.Empty, request.UserId, cancellationToken);

        settingsResolver.InvalidateCache(SettingScope.Instance, null);

        return BaseCommandResponse.Success(Guid.Empty, "Footer governance settings updated successfully.");
    }
}
