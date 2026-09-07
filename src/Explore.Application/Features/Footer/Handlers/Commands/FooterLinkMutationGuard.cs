using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;

namespace Explore.Application.Features.Footer.Handlers.Commands;

public sealed class FooterLinkMutationGuard(
    IHierarchicalSettingsResolver settingsResolver,
    IDeploymentModeProvider deploymentModeProvider)
{
    public async Task EnsureAllowedAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (await deploymentModeProvider.IsSingleTenantAsync(cancellationToken))
            return;

        var settings = await settingsResolver.ResolveGroupAsync<FooterSettingGroup>(
            new SettingContext(TenantId: tenantId),
            cancellationToken);

        if (settings.LockTenantLinkGroups)
            throw new AuthorizationException(ResourceKinds.Tenant, AuthorizationActions.Update);
    }
}
