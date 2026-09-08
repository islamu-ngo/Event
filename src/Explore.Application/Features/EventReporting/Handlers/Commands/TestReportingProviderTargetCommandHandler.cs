using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.EventReporting.Handlers.Commands;

public sealed class TestReportingProviderTargetCommandHandler(
    ITenantContext tenantContext,
    IAdminContext adminContext,
    IHierarchicalSettingsResolver settingsResolver,
    IReportingRoutingPolicyResolver routingPolicyResolver)
    : IRequestHandler<TestReportingProviderTargetCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        TestReportingProviderTargetCommand request,
        CancellationToken cancellationToken)
    {
        Guid tenantId = tenantContext.TenantId == Guid.Empty ? request.TenantId : tenantContext.TenantId;

        if (!await IsUserAuthorizedAsync(tenantId, request.UserId, cancellationToken))
        {
            return Failed(
                "Only tenant administrators or instance administrators can test moderation reporting providers.",
                FailureCodes.AdminRequired);
        }

        if (request.Provider is not EventReportExternalProvider.Osprey and not EventReportExternalProvider.Coop)
        {
            return Failed("Unsupported moderation reporting provider.");
        }

        TenantDelegationSettingGroup delegation = await settingsResolver.ResolveGroupAsync<TenantDelegationSettingGroup>(
            new SettingContext(tenantId),
            cancellationToken);

        BaseCommandResponse<Guid>? lockFailure = LockedIfNeeded(request.Provider, delegation);
        if (lockFailure is not null)
        {
            return lockFailure;
        }

        ReportingRoutingPolicy policy = await routingPolicyResolver.ResolveAsync(cancellationToken);
        ReportingProviderTarget? target = SelectTenantTarget(request.Provider, policy);
        if (target is null)
        {
            return Failed($"Tenant {request.Provider} reporting provider is not configured.");
        }

        if (string.IsNullOrWhiteSpace(target.EndpointUrl) || string.IsNullOrWhiteSpace(target.ApiKey))
        {
            return Failed($"Tenant {request.Provider} reporting provider requires an endpoint URL and API key before testing.");
        }

        return BaseCommandResponse.Success(
            tenantId,
            $"Tenant {request.Provider} reporting provider configuration is ready for test dispatch.");
    }

    private async Task<bool> IsUserAuthorizedAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (await adminContext.IsTenantAdminAsync(tenantId, cancellationToken))
        {
            return true;
        }

        IReadOnlyList<Guid> adminTenantIds = await adminContext.GetAdminTenantIdsAsync(userId, cancellationToken);
        if (adminTenantIds.Contains(tenantId))
        {
            return true;
        }

        return await adminContext.IsInstanceAdminAsync(userId, cancellationToken);
    }

    private static BaseCommandResponse<Guid>? LockedIfNeeded(
        EventReportExternalProvider provider,
        TenantDelegationSettingGroup delegation)
    {
        if (delegation.LockReportingProviders)
        {
            return Locked("Tenant moderation reporting provider tests are locked by instance policy.");
        }

        if (provider == EventReportExternalProvider.Osprey && delegation.LockTenantOspreyProvider)
        {
            return Locked("Tenant Osprey reporting provider tests are locked by instance policy.");
        }

        if (provider == EventReportExternalProvider.Coop && delegation.LockTenantCoopProvider)
        {
            return Locked("Tenant Coop reporting provider tests are locked by instance policy.");
        }

        return null;
    }

    private static ReportingProviderTarget? SelectTenantTarget(
        EventReportExternalProvider provider,
        ReportingRoutingPolicy policy)
    {
        IReadOnlyList<ReportingProviderTarget> targets = provider switch
        {
            EventReportExternalProvider.Osprey when policy.TenantOspreyEnabled => policy.OspreyTargets,
            EventReportExternalProvider.Coop when policy.TenantCoopEnabled => policy.CoopTargets,
            _ => []
        };

        return targets.FirstOrDefault(target => target.Scope == EventReportProviderTargetScope.Tenant);
    }

    private static BaseCommandResponse<Guid> Locked(string message) =>
        BaseCommandResponse.Failure<Guid>(
            FailureCodes.ReportingTenantOverridesLocked,
            message,
            ["Instance reporting delegation must be unlocked before tenant reporting provider tests can run."]);

    private static BaseCommandResponse<Guid> Failed(string message, string? failureCode = null) => failureCode is null
        ? BaseCommandResponse.Validation<Guid>([message], message)
        : failureCode == FailureCodes.AdminRequired
            ? BaseCommandResponse.Authorization<Guid>(message)
            : BaseCommandResponse.Failure<Guid>(failureCode, message, [message]);
}
