// ABOUTME: Applies a tenant plan assignment by copying version settings into tenant overrides.
// ABOUTME: Preflights quotas and locks, coordinating guarded policy settings inside the assignment transaction.

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
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
    IMediator mediator,
    IEmailDeliverySettingsWriter emailDeliverySettingsWriter,
    IVisitorAccessSettingsWriter visitorSettingsWriter)
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

        TenantPlanVersion? version = await tenantPlanRepository.GetVersionAsync(
            assignment.TenantPlanVersionId,
            cancellationToken);
        if (version is null)
        {
            return Failure(request.AssignmentId, "tenant_plan_version_not_found");
        }

        TenantPlanVersionSetting[] guardedSettings = version.Settings
            .Where(IsGuarded)
            .OrderBy(GuardedKeyOrder)
            .ToArray();
        TenantPlanVersionSetting[] unguardedSettings = version.Settings
            .Where(setting => !IsGuarded(setting) && !EmailDeliverySettingKeys.Contains(setting.SettingKey)
                && !VisitorAccessSettingMutationGuard.Handles(setting.SettingKey))
            .OrderBy(setting => setting.SettingKey, StringComparer.Ordinal)
            .ToArray();
        TenantPlanVersionSetting[] smtpSettings = version.Settings
            .Where(setting => EmailDeliverySettingKeys.Contains(setting.SettingKey))
            .ToArray();
        bool hasVisitorSettings = version.Settings.Any(setting => VisitorAccessSettingMutationGuard.Handles(setting.SettingKey));
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

        if (guardedSettings.Length == 0 && outerMutationKeys.Length == 0 && smtpSettings.Length == 0 && !hasVisitorSettings)
        {
            return Success(request.AssignmentId);
        }

        Task<(BaseCommandResponse<Guid> Response, IReadOnlyList<SettingChangedNotification> Notifications)>
            ApplyTransactionAsync(CancellationToken token) => smtpSettings.Length == 0 && !hasVisitorSettings
                ? unitOfWork.ExecuteInTransactionAsync(ApplyWithLocksAsync, token)
                : unitOfWork.ExecuteSerializableAsync(ApplyWithLocksAsync, token);

        Task<(BaseCommandResponse<Guid> Response, IReadOnlyList<SettingChangedNotification> Notifications)>
            ApplyWithLocksAsync(CancellationToken transactionToken) => outerMutationKeys.Length == 0
                    ? ApplyInsideTransactionAsync(
                        request,
                        assignment,
                        version,
                        guardedSettings,
                        smtpSettings,
                        unguardedUpserts,
                        unguardedMutationKeys,
                        transactionToken)
                    : mutationLock.ExecuteManyAsync(
                        outerMutationKeys,
                        innerToken => ApplyInsideTransactionAsync(
                            request,
                            assignment,
                            version,
                            guardedSettings,
                            smtpSettings,
                            unguardedUpserts,
                            unguardedMutationKeys,
                            innerToken),
                        transactionToken);

        (BaseCommandResponse<Guid> Response, IReadOnlyList<SettingChangedNotification> Notifications) outcome;
        try
        {
            outcome = smtpSettings.Length == 0 && !hasVisitorSettings
                ? await ApplyTransactionAsync(cancellationToken)
                : await mutationLock.ExecuteOrderedGroupsAsync(
                    [hasVisitorSettings ? Explore.Application.Services.VisitorAccessCapabilityResolver.AuthoritySettingKeys : [],
                     smtpSettings.Length > 0 ? EmailDeliverySettingKeys.All : [],
                     guardedSettings.Length > 0 ? PublicationPolicySettingKeys.All : [],
                     outerMutationKeys], ApplyTransactionAsync, cancellationToken);
        }
        catch (TenantPlanApplyRejectedException exception)
        {
            return exception.Response;
        }

        if (outcome.Notifications.Count > 0)
        {
            settingsResolver.InvalidateCache(SettingScope.Tenant, request.TenantId);
            foreach (SettingChangedNotification notification in outcome.Notifications)
            {
                await mediator.Publish(notification, CancellationToken.None);
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
            IReadOnlyList<TenantPlanVersionSetting> smtpSettings,
            IReadOnlyList<TenantSettingOverrideUpsert> unguardedUpserts,
            IReadOnlyList<string> unguardedSettingKeys,
            CancellationToken cancellationToken)
    {
        string? quotaError = await storageQuotaCeilingPolicy.ValidateAsync(version.Quotas, cancellationToken);
        if (quotaError is not null)
        {
            return (Failure(request.AssignmentId, quotaError), []);
        }

        foreach (string settingKey in unguardedSettingKeys.Concat(smtpSettings.Select(setting => setting.SettingKey)))
        {
            if (await systemSettingRepository.IsLocked(settingKey, cancellationToken))
            {
                return (Failure(request.AssignmentId, "tenant_plan_setting_locked"), []);
            }
        }

        DateTime occurredAtUtc = DateTime.UtcNow;
        var notifications = new List<SettingChangedNotification>();
        if (smtpSettings.Count > 0)
        {
            EmailDeliverySettingsWriteResult result = await emailDeliverySettingsWriter.ApplyAsync(
                [.. smtpSettings.Select(setting => new EmailDeliverySettingMutation(
                    TenantId: request.TenantId,
                    Key: setting.SettingKey,
                    Kind: EmailDeliverySettingMutationKind.SetValue,
                    Value: setting.JsonValue,
                    IsLocked: setting.IsLocked))],
                request.AppliedByUserId,
                cancellationToken);
            if (!result.IsAccepted())
                return (result.ToCommandResponse(request.AssignmentId, "Tenant plan applied."), []);

            notifications.AddRange(result.ToNotifications(request.AppliedByUserId));
        }

        var visitorSettings = version.Settings.Where(setting => VisitorAccessSettingMutationGuard.Handles(setting.SettingKey)).ToArray();
        if (visitorSettings.Length > 0)
        {
            var result = await visitorSettingsWriter.ApplyAsync(
                [.. visitorSettings.Select(setting => new VisitorAccessSettingMutation(request.TenantId, setting.SettingKey,
                    VisitorAccessSettingMutationKind.SetValue, setting.JsonValue, setting.IsLocked))],
                request.AppliedByUserId, cancellationToken);
            if (!result.Success)
                throw new TenantPlanApplyRejectedException(Failure(request.AssignmentId, result.FailureCode!));
            notifications.AddRange(result.DeferredNotifications);
        }

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
                throw new TenantPlanApplyRejectedException(Failure(request.AssignmentId, failureCode));
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

    private sealed class TenantPlanApplyRejectedException(BaseCommandResponse<Guid> response)
        : Exception("Tenant plan application was rejected.")
    {
        public BaseCommandResponse<Guid> Response { get; } = response;
    }
}
