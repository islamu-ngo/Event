using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.EmailDispatch;
using Explore.Application.Features.Settings.Handlers;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.InstanceAdmin.Handlers.Commands;

public sealed class SetInstanceTenantSettingCommandHandler(
    ITenantSettingRepository repository,
    ISystemSettingRepository systemSettingRepository,
    ISettingMutationLock mutationLock,
    ICurrentUserService currentUserService,
    IHierarchicalSettingsResolver settingsResolver,
    IEnumerable<Contracts.Operations.INotificationHandler<SettingChangedNotification>> notificationHandlers,
    IPublicationPolicyMutationBoundary publicationPolicyMutationBoundary,
    IEmailDeliverySettingsWriter emailDeliverySettingsWriter,
    IVisitorAccessSettingsWriter visitorSettings,
    IEventResourceSettingsWriter eventResourceSettingsWriter,
    IInstanceAdministratorCommitAuthority commitAuthority,
    IHumanCommitScopePreparation humanCommitScope,
    IHumanSettingsMutationPreparation settingsPreparation,
    ILocationPrivacyGovernanceMutationService locationPrivacyMutations,
    IHumanEmailMutationPreparation emailPreparation,
    IAtprotoFederationSettingsWriter federationSettings)
    : ICommandHandler<SetInstanceTenantSettingCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        SetInstanceTenantSettingCommand request,
        CancellationToken cancellationToken)
    {
        await humanCommitScope.PrepareAsync(cancellationToken);
        Guid? currentActorUserId = currentUserService.UserId;
        if (currentActorUserId is null || currentActorUserId == Guid.Empty)
        {
            return InstanceTenantSettingSecurity.Failure(
                request.TenantId,
                "authenticated_operator_required",
                "Authenticated operator context is required.");
        }

        Guid actorUserId = currentActorUserId.Value;
        BaseCommandResponse<Guid>? invalidTarget = InstanceTenantSettingSecurity.ValidateTarget(
            request.TenantId,
            request.Key,
            out SettingDefinition definition);
        if (invalidTarget is not null)
        {
            return invalidTarget;
        }

        (bool isValid, string? serializedValue, _) =
            SettingCommandHelper.ValidateAndSerialize(request.Value, definition);
        if (!isValid)
        {
            return InstanceTenantSettingSecurity.Failure(
                request.TenantId,
                "setting_validation_failed",
                "The tenant setting value is invalid.");
        }

        var prepared = EmailDeliverySettingKeys.Contains(request.Key) ? null
            : await settingsPreparation.PrepareAsync(request, serializedValue!, cancellationToken);
        Exception? primaryFailure = null;
        try
        {
            if (prepared is not null)
                SettingCommandHelper.RequireHumanSubject(humanCommitScope, actorUserId);
            if (VisitorAccessSettingMutationGuard.Handles(request.Key))
            {
                var result = await mutationLock.ExecuteOrderedGroupsAsync(
                    [Explore.Application.Services.VisitorAccessCapabilityResolver.AuthoritySettingKeys],
                    outerToken => settingsPreparation.ExecutePreparedAsync(prepared!, (_, token) => visitorSettings.ApplyAsync(
                    [new(request.TenantId, request.Key, VisitorAccessSettingMutationKind.SetValue, serializedValue)],
                    SettingCommandHelper.RequireHumanSubject(humanCommitScope, actorUserId),
                    SettingsMutationOperation.InstanceTenantSettings,
                    actorUserId, token), outerToken), cancellationToken);
                return await result.CompleteAsync(settingsResolver, notificationHandlers, SettingScope.Tenant, request.TenantId);
            }

            if (EventResourceSettingMutationGuard.Handles(request.Key))
            {
                EventResourceSettingsWriteResult result = await mutationLock.ExecuteOrderedGroupsAsync(
                    [EventResourceSettingMutationGuard.Keys],
                    outerToken => settingsPreparation.ExecutePreparedAsync(prepared!, (_, token) => eventResourceSettingsWriter.ApplyAsync(
                    [new EventResourceSettingMutation(request.TenantId, request.Key,
                    EventResourceSettingMutationKind.SetValue, serializedValue)],
                    SettingCommandHelper.RequireHumanSubject(humanCommitScope, actorUserId),
                    SettingsMutationOperation.InstanceTenantSettings,
                    actorUserId, token), outerToken), cancellationToken);
                return await result.CompleteAsync(
                    settingsResolver, notificationHandlers, SettingScope.Tenant, request.TenantId);
            }

            if (EmailDeliverySettingKeys.Contains(request.Key))
            {
                var email = await emailPreparation.PrepareAsync(request, serializedValue!, cancellationToken);
                EmailDeliverySettingsWriteResult result = await emailPreparation.ExecutePreparedAsync(email,
                    token => emailDeliverySettingsWriter.ApplyInstanceTenantAsync(
                    [new EmailDeliverySettingMutation(
                    TenantId: request.TenantId,
                    Key: request.Key,
                    Kind: EmailDeliverySettingMutationKind.SetValue,
                    Value: serializedValue)],
                    actorUserId,
                    token), cancellationToken);
                if (result.IsAccepted() && !result.Changes.IsEmpty)
                {
                    settingsResolver.InvalidateCache(SettingScope.Tenant, request.TenantId);
                    foreach (SettingChangedNotification notification in result.ToNotifications(actorUserId))
                        await notificationHandlers.HandleAsync(notification, CancellationToken.None);
                }

                return result.ToCommandResponse(request.TenantId, "Tenant setting updated.");
            }

            if (PublicationPolicySettingKeys.All.Contains(request.Key, StringComparer.Ordinal))
            {
                (BaseCommandResponse<Guid> Response, IReadOnlyList<SettingChangedNotification> Notifications) outcome =
                    await mutationLock.ExecuteOrderedGroupsAsync(
                        [PublicationPolicySettingKeys.All],
                        outerToken => settingsPreparation.ExecutePreparedAsync(
                        prepared!, async (_, token) =>
                        {
                            if (!await commitAuthority.IsInstanceAdministratorForCommitAsync(actorUserId, token))
                                return (BaseCommandResponse.Authorization<Guid>(
                                    "Current instance administrator authority is required."),
                                    (IReadOnlyList<SettingChangedNotification>)[]);
                            DateTime occurredAtUtc = DateTime.UtcNow;
                            PublicationPolicyMutationResult boundaryResult =
                                await publicationPolicyMutationBoundary.ApplyTenantAsync(
                                    new PublicationPolicyTenantMutationRequest(
                                        request.TenantId,
                                        actorUserId,
                                        occurredAtUtc,
                                        [new PublicationPolicySettingMutation(
                                        request.Key,
                                        PublicationPolicyMutationKind.Set,
                                        serializedValue,
                                        request.TenantId,
                                        IsLocked: null)],
                                        PublicationPolicyLockedSystemBehavior.Reject),
                                    token);
                            if (!boundaryResult.Success)
                            {
                                string failureCode = string.IsNullOrWhiteSpace(boundaryResult.FailureCode)
                                    ? "event_reporting_intake_policy_invalid"
                                    : boundaryResult.FailureCode;
                                return (
                                    InstanceTenantSettingSecurity.Failure(
                                        request.TenantId,
                                        failureCode,
                                        boundaryResult.Message),
                                    (IReadOnlyList<SettingChangedNotification>)[]);
                            }

                            return (
                                BaseCommandResponse.Success(
                                    request.TenantId,
                                    "Tenant setting updated."),
                                (IReadOnlyList<SettingChangedNotification>)boundaryResult.DeferredNotifications);
                        }, outerToken),
                        cancellationToken);

                if (outcome.Notifications.Count > 0)
                {
                    settingsResolver.InvalidateCache(SettingScope.Tenant, request.TenantId);
                    foreach (SettingChangedNotification notification in outcome.Notifications)
                    {
                        await notificationHandlers.HandleAsync(notification, CancellationToken.None);
                    }
                }

                return outcome.Response;
            }

            IReadOnlyList<LocationPrivacyProjectionIdentity> corrected = [];
            (BaseCommandResponse<Guid> Response, SettingChangedNotification? Notification) unguardedOutcome =
                await mutationLock.ExecuteOrderedGroupsAsync(
                [[request.Key]],
                outerToken => settingsPreparation.ExecutePreparedAsync<(BaseCommandResponse<Guid>, SettingChangedNotification?)>(
                prepared!, async (_, token) =>
                {
                    if (!await commitAuthority.IsInstanceAdministratorForCommitAsync(actorUserId, token))
                        return (BaseCommandResponse.Authorization<Guid>(
                            "Current instance administrator authority is required."), null);
                    if (AtprotoFederationSettingPolicy.IsDedicatedKey(request.Key))
                    {
                        var written = await federationSettings.ApplyCurrentAsync(new(
                            HumanSettingsOperation.InstanceTenantSettings, SettingScope.Tenant, request.TenantId, null,
                            request.Key, HumanSettingsMutationKind.SetValue, serializedValue), token);
                        if (!written.Applied)
                            return (InstanceTenantSettingSecurity.Failure(request.TenantId,
                                written.FailureCode ?? "federation_setting_not_applied", "The federation setting change was not applied."), null);
                        return (BaseCommandResponse.Success(request.TenantId, "Tenant setting updated."),
                            new SettingChangedNotification(request.Key, written.PreviousValue, written.CurrentValue,
                                SettingSource.TenantOverride, request.TenantId, actorUserId, DateTime.UtcNow));
                    }
                    if (await systemSettingRepository.IsLocked(request.Key, token))
                    {
                        return (InstanceTenantSettingSecurity.Failure(
                            request.TenantId, "setting_system_locked", "The setting is locked at system scope."), null);
                    }

                    TenantSetting? existing = await repository.GetByTenantAndKey(
                        request.TenantId, request.Key, token);
                    if (locationPrivacyMutations.Handles(request.Key))
                    {
                        var location = await locationPrivacyMutations.ExecuteAsync(request.Key, serializedValue!,
                            SettingScope.Tenant, request.TenantId, actorUserId, async persistToken =>
                            {
                                await repository.SetValueAsync(request.TenantId, request.Key, serializedValue!, persistToken, actorUserId);
                                return existing?.Value;
                            }, token);
                        if (!location.Accepted)
                            return (BaseCommandResponse.Validation<Guid>([location.Error!], location.Error), null);
                        corrected = location.CorrectedProjections;
                    }
                    else
                        await repository.SetValueAsync(request.TenantId, request.Key, serializedValue!, token, actorUserId);
                    BaseCommandResponse<Guid> response = BaseCommandResponse.Success(
                        request.TenantId,
                        "Tenant setting updated.");
                    var notification = new SettingChangedNotification(
                        request.Key,
                        existing?.Value,
                        serializedValue,
                        SettingSource.TenantOverride,
                        request.TenantId,
                        actorUserId,
                        DateTime.UtcNow);
                    return (response, notification);
                }, outerToken),
                cancellationToken);

            if (unguardedOutcome.Notification is not null)
            {
                if (locationPrivacyMutations.Handles(request.Key))
                    await locationPrivacyMutations.InvalidateMutationAsync(SettingScope.Tenant,
                        request.TenantId, corrected, CancellationToken.None);
                settingsResolver.InvalidateCache(SettingScope.Tenant, request.TenantId);
                await notificationHandlers.HandleAsync(unguardedOutcome.Notification, CancellationToken.None);
            }

            return unguardedOutcome.Response;
        }
        catch (Exception failure) { primaryFailure = failure; throw; }
        finally { if (prepared is not null) await settingsPreparation.CloseAsync(prepared, primaryFailure); }
    }
}
