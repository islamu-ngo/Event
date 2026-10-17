using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.Settings;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Application.Settings;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public class UpdateModuleSettingsCommandHandler(
    IAdminContext adminContext, IHumanSettingsMutationPreparation preparation, ITypedSettingsOriginalStore store,
    IEnumerable<INotificationHandler<SettingChangedNotification>> notificationHandlers)
    : ICommandHandler<UpdateModuleSettingsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateModuleSettingsCommand request, CancellationToken cancellationToken)
    {
        if (!await adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken))
            return GovernancePatchExecution.Denied();
        if (!request.Patch.HasChanges())
            return GovernancePatchExecution.Invalid("At least one module setting must be provided.");
        var prepared = await preparation.PrepareAsync(request, cancellationToken);
        await GovernancePatchExecution.ApplyAsync(preparation, store, prepared, notificationHandlers, cancellationToken);
        return BaseCommandResponse.Success(Guid.Empty, "Module settings updated successfully.");
    }
}

public class UpdateEventPolicyCommandHandler : ICommandHandler<UpdateEventPolicyCommand, BaseCommandResponse<Guid>>
{
    private readonly IAdminContext _adminContext;
    private readonly IInstanceGovernanceSettingService _service;
    private readonly IHumanSettingsMutationPreparation _preparation;
    private readonly ITypedSettingsOriginalStore _originalStore;
    private readonly IEnumerable<INotificationHandler<SettingChangedNotification>> _notificationHandlers;

    public UpdateEventPolicyCommandHandler(IAdminContext adminContext, IInstanceGovernanceSettingService service,
        IHumanSettingsMutationPreparation preparation, ITypedSettingsOriginalStore originalStore,
        IEnumerable<INotificationHandler<SettingChangedNotification>> notificationHandlers)
    {
        _adminContext = adminContext;
        _service = service;
        _preparation = preparation;
        _originalStore = originalStore;
        _notificationHandlers = notificationHandlers;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateEventPolicyCommand request, CancellationToken cancellationToken)
    {
        if (!await _adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken))
            return BaseCommandResponse.Authorization<Guid>(
                "Only instance administrators can update instance governance settings.");
        if (!request.Patch.HasChanges())
            return Invalid("At least one event policy setting must be provided.");

        var prepared = await _preparation.PrepareAsync(request, cancellationToken);
        PublicationPolicyMutationResult result = await _preparation.ExecutePreparedAsync(prepared,
            (_, ct) => _service.ApplyEventPolicyPatchAsync(
                _preparation.CurrentTypedOriginal
                    ?? throw new UnauthorizedAccessException("The original event-policy owner is required."),
                _originalStore, ct), cancellationToken);
        if (!result.Success)
        {
            string failureCode = string.IsNullOrWhiteSpace(result.FailureCode)
                ? "event_reporting_intake_policy_invalid"
                : result.FailureCode;
            string failureMessage = string.IsNullOrWhiteSpace(result.Message)
                ? PublicationPolicyMutationMessages.InvalidPolicy
                : result.Message;
            return BaseCommandResponse.Failure<Guid>(failureCode, failureMessage, [failureCode]);
        }

        foreach (SettingChangedNotification notification in result.DeferredNotifications)
            await _notificationHandlers.HandleAsync(notification, cancellationToken);
        return BaseCommandResponse.Success(Guid.Empty, "Event policy updated successfully.");
    }

    private static BaseCommandResponse<Guid> Invalid(string message) =>
        BaseCommandResponse.Validation<Guid>([message], message);
}

public class UpdateOrganizationPolicyCommandHandler(
    IAdminContext adminContext, IHumanSettingsMutationPreparation preparation, ITypedSettingsOriginalStore store,
    IEnumerable<INotificationHandler<SettingChangedNotification>> notificationHandlers)
    : ICommandHandler<UpdateOrganizationPolicyCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateOrganizationPolicyCommand request, CancellationToken cancellationToken)
    {
        if (!await adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken))
            return GovernancePatchExecution.Denied();
        if (!request.Patch.HasChanges())
            return GovernancePatchExecution.Invalid("At least one organization policy setting must be provided.");
        var prepared = await preparation.PrepareAsync(request, cancellationToken);
        await GovernancePatchExecution.ApplyAsync(preparation, store, prepared, notificationHandlers, cancellationToken);
        return BaseCommandResponse.Success(Guid.Empty, "Organization policy updated successfully.");
    }
}

public class UpdateBrandingSettingsCommandHandler(
    IAdminContext adminContext, IHumanSettingsMutationPreparation preparation, ITypedSettingsOriginalStore store,
    ISettingMutationLock mutationLock, IEnumerable<INotificationHandler<SettingChangedNotification>> notificationHandlers)
    : ICommandHandler<UpdateBrandingSettingsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateBrandingSettingsCommand request, CancellationToken cancellationToken)
    {
        if (!await adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken))
            return GovernancePatchExecution.Denied();
        if (!request.Patch.HasChanges())
            return GovernancePatchExecution.Invalid("At least one branding setting must be provided.");
        var prepared = await preparation.PrepareAsync(request, cancellationToken);
        await mutationLock.ExecuteOrderedGroupsAsync([TenantBrandingGovernanceMutationLockKeys.All], async ct =>
        {
            await GovernancePatchExecution.ApplyAsync(preparation, store, prepared, notificationHandlers, ct);
            return true;
        }, cancellationToken);
        return BaseCommandResponse.Success(Guid.Empty, "Branding settings updated successfully.");
    }
}

public class UpdateDomainSettingsCommandHandler(
    IAdminContext adminContext, IHumanSettingsMutationPreparation preparation, ITypedSettingsOriginalStore store,
    IEnumerable<INotificationHandler<SettingChangedNotification>> notificationHandlers)
    : ICommandHandler<UpdateDomainSettingsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateDomainSettingsCommand request, CancellationToken cancellationToken)
    {
        if (!await adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken))
            return GovernancePatchExecution.Denied();
        if (!request.Patch.HasChanges())
            return GovernancePatchExecution.Invalid("At least one domain setting must be provided.");
        var prepared = await preparation.PrepareAsync(request, cancellationToken);
        await GovernancePatchExecution.ApplyAsync(preparation, store, prepared, notificationHandlers, cancellationToken);
        return BaseCommandResponse.Success(Guid.Empty, "Domain settings updated successfully.");
    }
}

public class UpdateTenantDelegationSettingsCommandHandler(
    IAdminContext adminContext, IHumanSettingsMutationPreparation preparation, ITypedSettingsOriginalStore store,
    ISettingMutationLock mutationLock, IEnumerable<INotificationHandler<SettingChangedNotification>> notificationHandlers)
    : ICommandHandler<UpdateTenantDelegationSettingsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateTenantDelegationSettingsCommand request, CancellationToken cancellationToken)
    {
        if (!await adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken))
            return GovernancePatchExecution.Denied();
        if (!request.Patch.HasChanges())
            return GovernancePatchExecution.Invalid("At least one tenant delegation setting must be provided.");
        var prepared = await preparation.PrepareAsync(request, cancellationToken);
        async Task<bool> ApplyAsync(CancellationToken token)
        {
            await GovernancePatchExecution.ApplyAsync(preparation, store, prepared, notificationHandlers, token);
            return true;
        }
        if (request.Patch.LockTenantSmtp.HasValue)
            await mutationLock.ExecuteOrderedGroupsAsync([EmailDeliverySettingKeys.All], ApplyAsync, cancellationToken);
        else
            await ApplyAsync(cancellationToken);
        return BaseCommandResponse.Success(Guid.Empty, "Tenant delegation settings updated successfully.");
    }
}

public class UpdateAdminPortalSettingsCommandHandler(
    IAdminContext adminContext, IHumanSettingsMutationPreparation preparation, ITypedSettingsOriginalStore store,
    IEnumerable<INotificationHandler<SettingChangedNotification>> notificationHandlers)
    : ICommandHandler<UpdateAdminPortalSettingsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateAdminPortalSettingsCommand request, CancellationToken cancellationToken)
    {
        if (!await adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken))
            return GovernancePatchExecution.Denied();
        if (!request.Patch.HasChanges())
            return GovernancePatchExecution.Invalid("At least one admin portal setting must be provided.");
        var prepared = await preparation.PrepareAsync(request, cancellationToken);
        await GovernancePatchExecution.ApplyAsync(preparation, store, prepared, notificationHandlers, cancellationToken);
        return BaseCommandResponse.Success(Guid.Empty, "Admin portal settings updated successfully.");
    }
}

public class UpdateMcpGovernanceSettingsCommandHandler(
    IAdminContext adminContext, IHumanSettingsMutationPreparation preparation, ITypedSettingsOriginalStore store,
    IEnumerable<INotificationHandler<SettingChangedNotification>> notificationHandlers)
    : ICommandHandler<UpdateMcpGovernanceSettingsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateMcpGovernanceSettingsCommand request, CancellationToken cancellationToken)
    {
        if (!await adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken))
            return GovernancePatchExecution.Denied();
        if (!request.Patch.HasChanges())
            return GovernancePatchExecution.Invalid("At least one MCP governance setting must be provided.");
        var prepared = await preparation.PrepareAsync(request, cancellationToken);
        await GovernancePatchExecution.ApplyAsync(preparation, store, prepared, notificationHandlers, cancellationToken);
        return BaseCommandResponse.Success(Guid.Empty, "MCP governance settings updated successfully.");
    }
}

public class UpdateAiAssistantGovernanceSettingsCommandHandler(
    IAdminContext adminContext, IHumanSettingsMutationPreparation preparation, ITypedSettingsOriginalStore store,
    IEnumerable<INotificationHandler<SettingChangedNotification>> notificationHandlers)
    : ICommandHandler<UpdateAiAssistantGovernanceSettingsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateAiAssistantGovernanceSettingsCommand request, CancellationToken cancellationToken)
    {
        if (!await adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken))
            return GovernancePatchExecution.Denied();
        if (!request.Patch.HasChanges())
            return GovernancePatchExecution.Invalid("At least one AI Assistant governance setting must be provided.");
        if (request.Patch.ProviderConfiguration is { HasValue: true, Value: null })
            return GovernancePatchExecution.Invalid("AI provider configuration must be complete.");
        try
        {
            var prepared = await preparation.PrepareAsync(request, cancellationToken);
            await GovernancePatchExecution.ApplyAsync(preparation, store, prepared, notificationHandlers, cancellationToken);
        }
        catch (Explore.Application.Exceptions.ValidationException exception)
        {
            return BaseCommandResponse.Validation<Guid>(exception.Errors, "AI provider configuration is invalid.");
        }
        return BaseCommandResponse.Success(Guid.Empty, "AI Assistant governance settings updated successfully.");
    }
}

public class UpdateRenderPolicySettingsCommandHandler(
    IAdminContext adminContext, IHumanSettingsMutationPreparation preparation, ITypedSettingsOriginalStore store,
    IEnumerable<INotificationHandler<SettingChangedNotification>> notificationHandlers)
    : ICommandHandler<UpdateRenderPolicySettingsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateRenderPolicySettingsCommand request, CancellationToken cancellationToken)
    {
        if (!await adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken))
            return GovernancePatchExecution.Denied();
        if (!request.Patch.HasChanges())
            return GovernancePatchExecution.Invalid("At least one render policy setting must be provided.");
        try
        {
            var prepared = await preparation.PrepareAsync(request, cancellationToken);
            await GovernancePatchExecution.ApplyAsync(preparation, store, prepared, notificationHandlers, cancellationToken);
        }
        catch (Explore.Application.Exceptions.ValidationException exception)
        {
            return BaseCommandResponse.Validation<Guid>(exception.Errors, "Invalid render policy settings.");
        }
        return BaseCommandResponse.Success(Guid.Empty, "Render policy settings updated successfully.");
    }
}

internal static class GovernancePatchExecution
{
    internal static BaseCommandResponse<Guid> Denied() => BaseCommandResponse.Authorization<Guid>(
        "Only instance administrators can update instance governance settings.");
    internal static BaseCommandResponse<Guid> Invalid(string message) =>
        BaseCommandResponse.Validation<Guid>([message], message);

    internal static async Task ApplyAsync(IHumanSettingsMutationPreparation preparation,
        ITypedSettingsOriginalStore store, IPreparedHumanSettingsMutation prepared,
        IEnumerable<INotificationHandler<SettingChangedNotification>> notificationHandlers, CancellationToken token)
    {
        TypedSettingsOriginal? original = null;
        var notifications = await preparation.ExecutePreparedAsync(prepared, (_, ct) =>
        {
            original = preparation.CurrentTypedOriginal
                ?? throw new UnauthorizedAccessException("The acquired original governance owner is required.");
            return store.ApplyGovernanceAsync(original, ct);
        }, token);
        store.InvalidateGovernanceAfterCommit(original!);
        foreach (var notification in notifications)
            await notificationHandlers.HandleAsync(notification, CancellationToken.None);
    }
}
