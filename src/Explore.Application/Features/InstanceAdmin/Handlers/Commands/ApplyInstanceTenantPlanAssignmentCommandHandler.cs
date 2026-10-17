using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.Settings;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Settings;

namespace Explore.Application.Features.InstanceAdmin.Handlers.Commands;

public sealed class ApplyInstanceTenantPlanAssignmentCommandHandler(
    IHumanSettingsMutationPreparation preparation,
    ITypedSettingsOriginalStore store,
    ISettingMutationLock mutationLock,
    IHierarchicalSettingsResolver settingsResolver,
    IEnumerable<INotificationHandler<SettingChangedNotification>> notificationHandlers)
    : ICommandHandler<ApplyInstanceTenantPlanAssignmentCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        ApplyInstanceTenantPlanAssignmentCommand request, CancellationToken cancellationToken)
    {
        TenantPlanAssignmentApplication outcome;
        try
        {
            var prepared = await preparation.PrepareAsync(request, cancellationToken);
            var selection = prepared.PlanSelection;
            string[] outerKeys = selection.HasStorageQuota
                ? [.. selection.OrdinaryKeys, GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes]
                : [.. selection.OrdinaryKeys];

            Task<TenantPlanAssignmentApplication> ApplyAsync(CancellationToken token) =>
                ((ITypedTenantPlanOriginalStore)store).ApplyPlanAsync(
                    preparation.CurrentTypedOriginal
                        ?? throw new UnauthorizedAccessException("The original acquired plan is required."), token);
            Task<TenantPlanAssignmentApplication> ExecuteAsync(CancellationToken token) =>
                preparation.ExecutePreparedAsync(prepared, (_, inner) => outerKeys.Length == 0
                    ? ApplyAsync(inner) : mutationLock.ExecuteManyAsync(outerKeys, ApplyAsync, inner), token);

            outcome = !selection.HasEmail && !selection.HasVisitor
                ? await ExecuteAsync(cancellationToken)
                : await mutationLock.ExecuteOrderedGroupsAsync(
                    [selection.HasVisitor ? VisitorAccessCapabilityResolver.AuthoritySettingKeys : [],
                     selection.HasEmail ? EmailDeliverySettingKeys.All : [],
                     selection.HasPublication ? PublicationPolicySettingKeys.All : [],
                     outerKeys], ExecuteAsync, cancellationToken);
        }
        catch (TenantPlanOriginalRejectedException exception)
        {
            return exception.Response;
        }

        if (!outcome.Notifications.IsEmpty)
        {
            settingsResolver.InvalidateCache(SettingScope.Tenant, request.TenantId);
            foreach (var notification in outcome.Notifications)
                await notificationHandlers.HandleAsync(notification, CancellationToken.None);
        }
        return outcome.Response;
    }
}
