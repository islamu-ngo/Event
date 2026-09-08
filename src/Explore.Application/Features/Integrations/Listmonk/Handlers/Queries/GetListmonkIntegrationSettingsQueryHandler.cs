using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.DTOs.Integrations;
using Explore.Application.Features.Integrations.Listmonk.Requests.Queries;
using Explore.Application.Features.Settings.Handlers;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Secrets;
using Explore.Domain.Settings;
using MediatR;

namespace Explore.Application.Features.Integrations.Listmonk.Handlers.Queries;

public sealed class GetListmonkIntegrationSettingsQueryHandler(
    IHierarchicalSettingsResolver settingsResolver,
    ISecretResolver secretResolver,
    IAdminContext adminContext,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetListmonkIntegrationSettingsQuery, ListmonkIntegrationSettingsDto>
{
    public async Task<ListmonkIntegrationSettingsDto> Handle(
        GetListmonkIntegrationSettingsQuery request,
        CancellationToken cancellationToken)
    {
        var context = new SettingContext(TenantId: tenantContext.TenantId);
        var apiUsername = await secretResolver.ResolveAsync(
            SecretDefinitionRegistry.Keys.Integrations.Listmonk.ApiUsername,
            tenantContext.TenantId,
            cancellationToken);
        var apiKey = await secretResolver.ResolveAsync(
            SecretDefinitionRegistry.Keys.Integrations.Listmonk.ApiKey,
            tenantContext.TenantId,
            cancellationToken);

        var canEdit = currentUserService.IsAuthenticated &&
            (await SettingCommandHelper.CheckAuthorizationAsync(
                SettingScope.Tenant,
                adminContext,
                tenantContext,
                currentUserService,
                cancellationToken)).Authorized;

        return new ListmonkIntegrationSettingsDto
        {
            Enabled = await settingsResolver.ResolveAsync<bool>(
                GovernanceSettingKeys.Integrations.Listmonk.Enabled,
                context,
                cancellationToken),
            InstanceUrl = await settingsResolver.ResolveAsync<string>(
                GovernanceSettingKeys.Integrations.Listmonk.InstanceUrl,
                context,
                cancellationToken),
            DefaultListId = await settingsResolver.ResolveAsync<int>(
                GovernanceSettingKeys.Integrations.Listmonk.DefaultListId,
                context,
                cancellationToken),
            PreconfirmSubscriptions = await settingsResolver.ResolveAsync<bool>(
                GovernanceSettingKeys.Integrations.Listmonk.PreconfirmSubscriptions,
                context,
                cancellationToken),
            SyncOnRegistration = await settingsResolver.ResolveAsync<bool>(
                GovernanceSettingKeys.Integrations.Listmonk.SyncOnRegistration,
                context,
                cancellationToken),
            ApiUsernameConfigured = apiUsername.IsResolved,
            ApiKeyConfigured = apiKey.IsResolved,
            CanEdit = canEdit
        };
    }
}
