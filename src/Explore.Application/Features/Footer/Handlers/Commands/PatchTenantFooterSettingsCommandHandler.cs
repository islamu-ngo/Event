using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.DTOs.Footer.Validators;
using Explore.Application.Features.Footer.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Footer.Handlers.Commands;

public sealed class PatchTenantFooterSettingsCommandHandler(
    IHierarchicalSettingsResolver settingsResolver,
    ITenantSettingRepository tenantSettingRepository,
    IFooterSettingsWriter footerWriter,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IUserContext userContext,
    IAdminContext adminContext,
    IInstanceAdministratorCommitAuthority instanceAuthority,
    ITenantAdministratorCommitAuthority tenantAuthority,
    ISystemSettingRepository systemSettings,
    ISettingMutationLock mutationLock,
    RequestAuthorization<PatchTenantFooterSettingsCommand> authorization,
    IFooterOperationExecution footerExecution,
    ILogger<PatchTenantFooterSettingsCommandHandler> logger)
    : ICommandHandler<PatchTenantFooterSettingsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        PatchTenantFooterSettingsCommand request, CancellationToken cancellationToken)
    {
        var human = await footerExecution.RequireOwnedAsync(request, cancellationToken);
        Guid tenantId = tenantContext.TenantId;
        Guid? userId = await adminContext.ResolveUserIdAsync(cancellationToken);
        if (!userContext.IsAuthenticated || tenantId == Guid.Empty || userId is null)
            return BaseCommandResponse.Authentication<Guid>();
        var validator = new PatchTenantFooterSettingsDtoValidator();
        var validationResult = await validator.ValidateAsync(request.Patch, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(error => error.ErrorMessage),
                "Tenant footer settings patch failed.");
        }

        SettingDefinition[] governance =
        [
            FooterSettingDefinitions.LockTenantTemplate, FooterSettingDefinitions.LockTenantDescription,
            FooterSettingDefinitions.LockTenantSocialLinks, FooterSettingDefinitions.LockTenantCopyright
        ];
        var authoritativeRequest = request with { TenantId = tenantId };
        async Task<bool> Patch(CancellationToken transactionToken)
        {
            await authorization.AuthorizeAsync(authoritativeRequest, logger, transactionToken);
            bool allowed = await instanceAuthority.IsInstanceAdministratorForCommitAsync(userId.Value, transactionToken)
                || await tenantAuthority.IsTenantAdministratorForCommitAsync(userId.Value, tenantId, transactionToken);
            if (!allowed)
                throw new AuthorizationException(ResourceKinds.Tenant, AuthorizationActions.Update);
            var locked = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in governance)
            {
                var stored = await systemSettings.GetByKey(definition.Key, transactionToken);
                if (!bool.TryParse(stored?.Value ?? definition.DefaultValue, out bool isLocked))
                    throw new AuthorizationException(ResourceKinds.Tenant, AuthorizationActions.Update);
                if (isLocked)
                    locked.Add(definition.Key);
            }
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

            if (request.Patch.Template?.Value is { HasValue: true } template && !locked.Contains(FooterSettingDefinitions.LockTenantTemplate.Key))
                writes.Add(new TenantSettingOverrideUpsert(
                    GovernanceSettingKeys.Footer.Template,
                    SettingValueSerializer.Serialize(template.Value!),
                    IsLocked: false));

            if (request.Patch.Description?.Show is { HasValue: true } showDescription && !locked.Contains(FooterSettingDefinitions.LockTenantDescription.Key))
                writes.Add(new TenantSettingOverrideUpsert(
                    GovernanceSettingKeys.Footer.ShowDescription,
                    SettingValueSerializer.Serialize(showDescription.Value),
                    IsLocked: false));

            if (request.Patch.Description?.Text is { HasValue: true } descriptionText && !locked.Contains(FooterSettingDefinitions.LockTenantDescription.Key))
                writes.Add(new TenantSettingOverrideUpsert(
                    GovernanceSettingKeys.Footer.DescriptionText,
                    SettingValueSerializer.Serialize(descriptionText.Value!),
                    IsLocked: false));

            if (request.Patch.SocialLinks?.Show is { HasValue: true } showSocialLinks && !locked.Contains(FooterSettingDefinitions.LockTenantSocialLinks.Key))
                writes.Add(new TenantSettingOverrideUpsert(
                    GovernanceSettingKeys.Footer.ShowSocialLinks,
                    SettingValueSerializer.Serialize(showSocialLinks.Value),
                    IsLocked: false));

            if (request.Patch.SocialLinks?.Items is { HasValue: true } socialLinks && !locked.Contains(FooterSettingDefinitions.LockTenantSocialLinks.Key))
                writes.Add(new TenantSettingOverrideUpsert(
                    GovernanceSettingKeys.Footer.SocialLinks,
                    SettingValueSerializer.Serialize(socialLinks.Value!),
                    IsLocked: false));

            if (request.Patch.Copyright?.Text is { HasValue: true } copyrightText && !locked.Contains(FooterSettingDefinitions.LockTenantCopyright.Key))
                writes.Add(new TenantSettingOverrideUpsert(
                    GovernanceSettingKeys.Footer.CopyrightText,
                    SettingValueSerializer.Serialize(copyrightText.Value!),
                    IsLocked: false));

            if (writes.Count > 0)
            {
                if (human is not null)
                    await footerWriter.WriteTenantAsync(request, writes, transactionToken);
                else
                    await tenantSettingRepository.UpsertManyForTenantAsync(
                        tenantId,
                        writes,
                        userId.Value,
                        transactionToken);
            }
            return true;
        }
        if (human is not null)
            await Patch(cancellationToken);
        else
            await mutationLock.ExecuteOrderedGroupsAsync(
            [governance.Select(definition => definition.Key).ToArray()],
            token => unitOfWork.ExecuteSerializableAsync(Patch, token),
            cancellationToken);

        if (human is null)
            settingsResolver.InvalidateCache(SettingScope.Tenant, tenantId);

        return BaseCommandResponse.Success(tenantId, "Tenant footer settings patched successfully.");
    }
}
