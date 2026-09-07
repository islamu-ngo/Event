using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ControlPlane.Plans;
using Explore.Application.Features.ControlPlane.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using MediatR;

namespace Explore.Application.Features.ControlPlane.Handlers.Commands;

public sealed class ApplyControlPlaneTenantPlanAssignmentCommandHandler(
    ITenantPlanRepository tenantPlanRepository,
    ITenantSettingRepository tenantSettingRepository,
    ISystemSettingRepository systemSettingRepository,
    TenantPlanStorageQuotaCeilingPolicy storageQuotaCeilingPolicy,
    IUnitOfWork unitOfWork,
    ISettingMutationLock mutationLock,
    IPublicationPolicyMutationBoundary publicationPolicyMutationBoundary,
    IHierarchicalSettingsResolver settingsResolver,
    IMediator mediator)
    : IRequestHandler<ApplyControlPlaneTenantPlanAssignmentCommand, BaseCommandResponse<Guid>>
{
    private const string InvalidPublicationPolicyCode = "event_reporting_intake_policy_invalid";

    public async Task<BaseCommandResponse<Guid>> Handle(
        ApplyControlPlaneTenantPlanAssignmentCommand request,
        CancellationToken cancellationToken)
    {
        TenantPlanAssignment? assignment = await tenantPlanRepository.GetAssignmentAsync(
            request.AssignmentId,
            cancellationToken);

        if (assignment is null)
        {
            return Failure(request.AssignmentId, "tenant_plan_assignment_not_found");
        }

        if (assignment.TenantId != request.TenantId)
        {
            return Failure(request.AssignmentId, "tenant_plan_assignment_tenant_mismatch");
        }

        if (assignment.TenantPlanAssignmentStatusId != (int)TenantPlanAssignmentStatusEnum.Active)
        {
            return Failure(request.AssignmentId, "tenant_plan_assignment_not_active");
        }

        TenantPlanVersion version = assignment.TenantPlanVersion;
        TenantPlanVersionSetting[] guardedSettings = version.Settings
            .Where(IsGuarded)
            .OrderBy(GuardedKeyOrder)
            .ToArray();
        TenantPlanVersionSetting[] unguardedSettings = version.Settings
            .Where(setting => !IsGuarded(setting))
            .OrderBy(setting => setting.SettingKey, StringComparer.Ordinal)
            .ToArray();
        TenantSettingOverrideUpsert[] unguardedUpserts = unguardedSettings
            .Select(setting => new TenantSettingOverrideUpsert(setting.SettingKey, setting.JsonValue, setting.IsLocked))
            .ToArray();
        string[] unguardedMutationKeys = unguardedUpserts
            .Select(upsert => upsert.SettingKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        bool hasStorageQuota = version.Quotas.Any(quota => quota.QuotaKey == TenantPlanQuotaKeys.StorageBytes);
        string[] outerMutationKeys = hasStorageQuota
            ? [.. unguardedMutationKeys, GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes]
            : unguardedMutationKeys;

        if (guardedSettings.Length == 0 && outerMutationKeys.Length == 0)
        {
            return Success(request.AssignmentId);
        }

        (BaseCommandResponse<Guid> Response, IReadOnlyList<SettingChangedNotification> Notifications) outcome =
            await unitOfWork.ExecuteInTransactionAsync(
                token => outerMutationKeys.Length == 0
                    ? ApplyInsideTransactionAsync(
                        request,
                        assignment,
                        version,
                        guardedSettings,
                        unguardedUpserts,
                        unguardedMutationKeys,
                        token)
                    : mutationLock.ExecuteManyAsync(
                        outerMutationKeys,
                        innerToken => ApplyInsideTransactionAsync(
                            request,
                            assignment,
                            version,
                            guardedSettings,
                            unguardedUpserts,
                            unguardedMutationKeys,
                            innerToken),
                        token),
                cancellationToken);

        if (outcome.Notifications.Count > 0)
        {
            settingsResolver.InvalidateCache(SettingScope.Tenant, request.TenantId);
            foreach (SettingChangedNotification notification in outcome.Notifications)
            {
                await mediator.Publish(notification, cancellationToken);
            }
        }

        return outcome.Response;
    }

    private async Task<(BaseCommandResponse<Guid> Response, IReadOnlyList<SettingChangedNotification> Notifications)>
        ApplyInsideTransactionAsync(
            ApplyControlPlaneTenantPlanAssignmentCommand request,
            TenantPlanAssignment assignment,
            TenantPlanVersion version,
            IReadOnlyList<TenantPlanVersionSetting> guardedSettings,
            IReadOnlyList<TenantSettingOverrideUpsert> unguardedUpserts,
            IReadOnlyList<string> unguardedSettingKeys,
            CancellationToken cancellationToken)
    {
        string? quotaError = await storageQuotaCeilingPolicy.ValidateAsync(version.Quotas, cancellationToken);
        if (quotaError is not null)
        {
            return (Failure(request.AssignmentId, quotaError), []);
        }

        foreach (string settingKey in unguardedSettingKeys)
        {
            if (await systemSettingRepository.IsLocked(settingKey, cancellationToken))
            {
                return (Failure(request.AssignmentId, "tenant_plan_setting_locked"), []);
            }
        }

        DateTime occurredAtUtc = DateTime.UtcNow;
        var notifications = new List<SettingChangedNotification>();
        if (guardedSettings.Count > 0)
        {
            PublicationPolicyMutationResult boundaryResult = await publicationPolicyMutationBoundary.ApplyTenantAsync(
                new PublicationPolicyTenantMutationRequest(
                    request.TenantId,
                    request.AppliedByUserId,
                    occurredAtUtc,
                    [.. guardedSettings.Select(setting => new PublicationPolicySettingMutation(
                        setting.SettingKey,
                        PublicationPolicyMutationKind.Set,
                        setting.JsonValue,
                        request.TenantId,
                        IsLocked: null))],
                    PublicationPolicyLockedSystemBehavior.Reject),
                cancellationToken);
            if (!boundaryResult.Success)
            {
                string failureCode = string.IsNullOrWhiteSpace(boundaryResult.FailureCode)
                    ? InvalidPublicationPolicyCode
                    : boundaryResult.FailureCode;
                return (Failure(request.AssignmentId, failureCode), []);
            }

            notifications.AddRange(boundaryResult.DeferredNotifications);
        }

        foreach (TenantSettingOverrideUpsert upsert in unguardedUpserts)
        {
            TenantSetting? existing = await tenantSettingRepository.GetByTenantAndKey(
                request.TenantId,
                upsert.SettingKey,
                cancellationToken);
            notifications.Add(new SettingChangedNotification(
                upsert.SettingKey,
                existing?.Value,
                upsert.Value,
                upsert.IsLocked ? SettingSource.TenantLocked : SettingSource.TenantOverride,
                request.TenantId,
                request.AppliedByUserId,
                occurredAtUtc));
        }

        if (unguardedUpserts.Count > 0)
        {
            await tenantSettingRepository.UpsertManyForTenantAsync(
                request.TenantId,
                unguardedUpserts,
                request.AppliedByUserId,
                cancellationToken);
        }

        assignment.UpdatedAt = occurredAtUtc;
        assignment.UpdatedBy = request.AppliedByUserId;
        await tenantPlanRepository.UpdateAssignmentAsync(assignment, cancellationToken);
        return (Success(request.AssignmentId), notifications);
    }

    private static bool IsGuarded(TenantPlanVersionSetting setting) =>
        PublicationPolicySettingKeys.All.Contains(setting.SettingKey, StringComparer.Ordinal);

    private static int GuardedKeyOrder(TenantPlanVersionSetting setting)
    {
        for (int index = 0; index < PublicationPolicySettingKeys.All.Count; index++)
        {
            if (string.Equals(PublicationPolicySettingKeys.All[index], setting.SettingKey, StringComparison.Ordinal))
                return index;
        }

        return int.MaxValue;
    }

    private static BaseCommandResponse<Guid> Success(Guid assignmentId) =>
        BaseCommandResponse.Success(assignmentId, "Tenant plan applied.");

    private static BaseCommandResponse<Guid> Failure(Guid assignmentId, string error) =>
        BaseCommandResponse.Failure(error, error, [error], assignmentId);
}
