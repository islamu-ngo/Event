using System.Data.Common;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Domain.Constants;
using Explore.Domain.Services;

namespace Explore.Application.Services;

public sealed class VisitorTenantReadAccessPolicy(
    IInstanceTenantSettingsReader settings,
    ITenantLifecycleAccessService lifecycle,
    IUserContext userContext) : IVisitorTenantReadAccessPolicy
{
    private static readonly string[] ReadSettingKeys = [GovernanceSettingKeys.PublicExperience.VisitorAccessMode];

    public Task<VisitorTenantReadAccessDecision> CheckAsync(Guid? tenantId, CancellationToken cancellationToken = default) =>
        EvaluateAsync(tenantId, userContext.IsAuthenticated, cancellationToken);

    public Task<VisitorTenantReadAccessDecision> CheckPublicAsync(Guid? tenantId, CancellationToken cancellationToken = default) =>
        EvaluateAsync(tenantId, false, cancellationToken);

    public async Task RequireAsync(Guid? tenantId, CancellationToken cancellationToken = default)
    {
        var decision = await CheckAsync(tenantId, cancellationToken);
        if (decision != VisitorTenantReadAccessDecision.Allowed)
            throw new TenantVisitorReadAccessException(decision);
    }

    private async Task<VisitorTenantReadAccessDecision> EvaluateAsync(
        Guid? tenantId, bool authenticated, CancellationToken cancellationToken)
    {
        if (tenantId is not { } selected || selected == Guid.Empty)
            return VisitorTenantReadAccessDecision.NotFound;

        try
        {
            if (!await lifecycle.IsPublicAsync(selected, cancellationToken))
                return VisitorTenantReadAccessDecision.NotFound;

            var current = await settings.ReadAsync(selected, ReadSettingKeys, cancellationToken);
            var instance = current.System.SingleOrDefault(setting =>
                setting.SettingKey == GovernanceSettingKeys.PublicExperience.VisitorAccessMode);
            var tenant = current.Tenant.SingleOrDefault(setting =>
                setting.SettingKey == GovernanceSettingKeys.PublicExperience.VisitorAccessMode);
            var mode = VisitorReadPolicy.ResolveMode(instance?.Value, instance?.IsLocked == true, tenant?.Value);
            return VisitorReadPolicy.AllowsRead(mode, authenticated)
                ? VisitorTenantReadAccessDecision.Allowed
                : VisitorTenantReadAccessDecision.AuthenticationRequired;
        }
        catch (Exception exception) when (exception is InvalidOperationException or DbException or TimeoutException)
        {
            return VisitorTenantReadAccessDecision.Unavailable;
        }
    }
}
