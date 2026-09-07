using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Footer.Validators;
using Explore.Application.Features.Footer.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using MediatR;

namespace Explore.Application.Features.Footer.Handlers.Commands;

public sealed class PatchTenantFooterSettingsCommandHandler(
    IHierarchicalSettingsResolver settingsResolver,
    ITenantSettingRepository tenantSettingRepository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<PatchTenantFooterSettingsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        PatchTenantFooterSettingsCommand request, CancellationToken cancellationToken)
    {
        var validator = new PatchTenantFooterSettingsDtoValidator();
        var validationResult = await validator.ValidateAsync(request.Patch, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(error => error.ErrorMessage),
                "Tenant footer settings patch failed.");
        }

        var tenantId = request.TenantId;
        var userId = request.UserId;
        var lockGroup = await settingsResolver.ResolveGroupAsync<FooterSettingGroup>(
            new SettingContext(), cancellationToken);
        var writes = new List<TenantSettingOverrideUpsert>();

        if (request.Patch.General?.Enabled is { HasValue: true } enabled)
            writes.Add(new TenantSettingOverrideUpsert(
                GovernanceSettingKeys.Footer.Enabled,
                SettingValueSerializer.Serialize(enabled.Value),
                IsLocked: false));

        if (request.Patch.General?.ShowCookieSettingsLink is { HasValue: true } showCookieSettingsLink)
            writes.Add(new TenantSettingOverrideUpsert(
                GovernanceSettingKeys.Footer.ShowCookieSettingsLink,
                SettingValueSerializer.Serialize(showCookieSettingsLink.Value),
                IsLocked: false));

        if (request.Patch.Template?.Value is { HasValue: true } template && !lockGroup.LockTenantTemplate)
            writes.Add(new TenantSettingOverrideUpsert(
                GovernanceSettingKeys.Footer.Template,
                SettingValueSerializer.Serialize(template.Value!),
                IsLocked: false));

        if (request.Patch.Description?.Show is { HasValue: true } showDescription && !lockGroup.LockTenantDescription)
            writes.Add(new TenantSettingOverrideUpsert(
                GovernanceSettingKeys.Footer.ShowDescription,
                SettingValueSerializer.Serialize(showDescription.Value),
                IsLocked: false));

        if (request.Patch.Description?.Text is { HasValue: true } descriptionText && !lockGroup.LockTenantDescription)
            writes.Add(new TenantSettingOverrideUpsert(
                GovernanceSettingKeys.Footer.DescriptionText,
                SettingValueSerializer.Serialize(descriptionText.Value!),
                IsLocked: false));

        if (request.Patch.SocialLinks?.Show is { HasValue: true } showSocialLinks && !lockGroup.LockTenantSocialLinks)
            writes.Add(new TenantSettingOverrideUpsert(
                GovernanceSettingKeys.Footer.ShowSocialLinks,
                SettingValueSerializer.Serialize(showSocialLinks.Value),
                IsLocked: false));

        if (request.Patch.SocialLinks?.Items is { HasValue: true } socialLinks && !lockGroup.LockTenantSocialLinks)
            writes.Add(new TenantSettingOverrideUpsert(
                GovernanceSettingKeys.Footer.SocialLinks,
                SettingValueSerializer.Serialize(socialLinks.Value!),
                IsLocked: false));

        if (request.Patch.Copyright?.Text is { HasValue: true } copyrightText && !lockGroup.LockTenantCopyright)
            writes.Add(new TenantSettingOverrideUpsert(
                GovernanceSettingKeys.Footer.CopyrightText,
                SettingValueSerializer.Serialize(copyrightText.Value!),
                IsLocked: false));

        if (writes.Count > 0)
        {
            await unitOfWork.ExecuteInTransactionAsync(
                ct => tenantSettingRepository.UpsertManyForTenantAsync(
                    tenantId,
                    writes,
                    userId,
                    ct),
                cancellationToken);
        }

        settingsResolver.InvalidateCache(SettingScope.Tenant, tenantId);

        return BaseCommandResponse.Success(tenantId, "Tenant footer settings patched successfully.");
    }
}
