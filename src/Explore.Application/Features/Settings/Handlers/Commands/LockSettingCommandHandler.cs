namespace Explore.Application.Features.Settings.Handlers.Commands;

using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain.Settings;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Logging;

public class LockSettingCommandHandler
    : ICommandHandler<LockSettingCommand, BaseCommandResponse<Guid>>
{
    private readonly IHierarchicalSettingsResolver _resolver;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAdminContext _adminContext;
    private readonly ICerbosConfigResolver? _cerbosConfigResolver;
    private readonly IEnumerable<Contracts.Operations.INotificationHandler<SettingChangedNotification>> _notificationHandlers;
    private readonly ILogger<LockSettingCommandHandler> _logger;
    private readonly IPublicationPolicyMutationBoundary _publicationPolicyMutationBoundary;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISettingMutationLock _mutationLock;
    private readonly IEmailDeliverySettingsWriter _emailSettingsWriter;
    private readonly IVisitorAccessSettingsWriter _visitorSettings;
    private readonly IEventResourceSettingsWriter _eventResourceSettingsWriter;
    private readonly IInstanceAdministratorCommitAuthority _instanceCommitAuthority;
    private readonly ITenantAdministratorCommitAuthority _tenantCommitAuthority;
    private readonly IHumanSubjectCommitAuthority _subjectCommitAuthority;
    private readonly IHumanCommitScopePreparation _humanCommitScope;
    private readonly IHumanSettingsMutationPreparation _settingsPreparation;
    private readonly ISystemSettingRepository _systemSettings;

    public LockSettingCommandHandler(
        IHierarchicalSettingsResolver resolver,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        IAdminContext adminContext,
        IEnumerable<Contracts.Operations.INotificationHandler<SettingChangedNotification>> notificationHandlers,
        ILogger<LockSettingCommandHandler> logger,
        IPublicationPolicyMutationBoundary publicationPolicyMutationBoundary,
        IUnitOfWork unitOfWork,
        ISettingMutationLock mutationLock,
        IEmailDeliverySettingsWriter emailSettingsWriter,
        IVisitorAccessSettingsWriter visitorSettings,
        IEventResourceSettingsWriter eventResourceSettingsWriter,
        IInstanceAdministratorCommitAuthority instanceCommitAuthority,
        ITenantAdministratorCommitAuthority tenantCommitAuthority,
        IHumanSubjectCommitAuthority subjectCommitAuthority,
        IHumanCommitScopePreparation humanCommitScope,
        IHumanSettingsMutationPreparation settingsPreparation,
        ISystemSettingRepository systemSettings,
        ICerbosConfigResolver? cerbosConfigResolver = null)
    {
        _resolver = resolver;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _adminContext = adminContext;
        _cerbosConfigResolver = cerbosConfigResolver;
        _notificationHandlers = notificationHandlers;
        _logger = logger;
        _publicationPolicyMutationBoundary = publicationPolicyMutationBoundary;
        _unitOfWork = unitOfWork;
        _mutationLock = mutationLock;
        _emailSettingsWriter = emailSettingsWriter;
        _visitorSettings = visitorSettings;
        _eventResourceSettingsWriter = eventResourceSettingsWriter;
        _instanceCommitAuthority = instanceCommitAuthority;
        _tenantCommitAuthority = tenantCommitAuthority;
        _subjectCommitAuthority = subjectCommitAuthority;
        _humanCommitScope = humanCommitScope;
        _settingsPreparation = settingsPreparation;
        _systemSettings = systemSettings;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        LockSettingCommand request, CancellationToken cancellationToken)
    {
        // Validate key exists
        var definition = SettingRegistry.Get(request.Key);
        if (definition is null)
        {
            string message = $"Setting key '{request.Key}' not found in registry.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        // Validate lockable
        if (!definition.IsLockable)
        {
            string message = $"Setting '{request.Key}' is not lockable.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        // Validate scope (only Instance and Tenant supported)
        if (request.Scope is not SettingScope.Instance and not SettingScope.Tenant)
        {
            string message = $"Locking is only supported at Instance and Tenant scopes, not {request.Scope}.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        // Authorization
        var (authorized, authError) = await SettingCommandHelper.CheckAuthorizationAsync(
            request.Scope, _adminContext, _tenantContext, _currentUserService, cancellationToken);
        if (!authorized)
        {
            return BaseCommandResponse.Authorization<Guid>(authError);
        }

        Guid? resolvedUserId = await SettingCommandHelper.ResolveCurrentUserIdAsync(
            _adminContext, _currentUserService, cancellationToken);
        var (scopeId, actorId) = SettingCommandHelper.GetScopeAndActorIds(
            request.Scope, _tenantContext, resolvedUserId);

        if (VisitorAccessSettingMutationGuard.Handles(request.Key))
        {
            var result = await _visitorSettings.ApplyAsync(
                [new(request.Scope == SettingScope.Tenant ? scopeId : null, request.Key,
                    VisitorAccessSettingMutationKind.SetLock, IsLocked: true)],
                SettingCommandHelper.RequireHumanSubject(_humanCommitScope, resolvedUserId),
                request.Scope == SettingScope.Instance ? SettingsMutationOperation.InstanceSettings : SettingsMutationOperation.TenantSettings,
                actorId, cancellationToken);
            return await result.CompleteAsync(_resolver, _notificationHandlers, request.Scope, scopeId);
        }

        if (EventResourceSettingMutationGuard.Handles(request.Key))
        {
            EventResourceSettingsWriteResult result = await _eventResourceSettingsWriter.ApplyAsync(
                [new EventResourceSettingMutation(
                    request.Scope == SettingScope.Tenant ? scopeId : null,
                    request.Key, EventResourceSettingMutationKind.SetLock, IsLocked: true)],
                SettingCommandHelper.RequireHumanSubject(_humanCommitScope, resolvedUserId),
                request.Scope == SettingScope.Instance ? SettingsMutationOperation.InstanceSettings : SettingsMutationOperation.TenantSettings,
                actorId, cancellationToken);
            return await result.CompleteAsync(
                _resolver, _notificationHandlers, request.Scope, scopeId);
        }

        if (EmailDeliverySettingKeys.Contains(request.Key))
        {
            var result = await _emailSettingsWriter.ApplyAsync(
                [new EmailDeliverySettingMutation(TenantId: request.Scope == SettingScope.Tenant ? scopeId : null,
                    Key: definition.Key, Kind: EmailDeliverySettingMutationKind.SetLock, IsLocked: true)],
                actorUserId: actorId, cancellationToken: cancellationToken);
            if (result.IsAccepted())
                foreach (var notification in result.ToNotifications(actorId))
                    await _notificationHandlers.HandleAsync(notification, CancellationToken.None);
            return result.ToCommandResponse(scopeId, "SMTP setting locked.");
        }

        bool isGuardedPublicationPolicyMutation = PublicationPolicySettingKeys.All
            .Contains(request.Key, StringComparer.Ordinal);
        if (isGuardedPublicationPolicyMutation && request.Scope == SettingScope.Tenant)
        {
            return BaseCommandResponse.Failure<Guid>(
                "setting_not_lockable",
                $"Setting '{request.Key}' cannot be locked at Tenant scope.");
        }

        if (isGuardedPublicationPolicyMutation)
        {
            var publicationPreparation = await _settingsPreparation.PrepareAsync(request, cancellationToken);
            SettingCommandHelper.RequireHumanSubject(_humanCommitScope, actorId);
            DateTime occurredAtUtc = DateTime.UtcNow;
            PublicationPolicyMutationResult mutationResult = await _mutationLock.ExecuteOrderedGroupsAsync(
                [PublicationPolicySettingKeys.All],
                outerToken => _settingsPreparation.ExecutePreparedAsync(publicationPreparation, async (_, token) =>
                {
                    var resolved = await _systemSettings.GetByKey(request.Key, token);
                    string value = resolved?.Value ?? definition.DefaultValue;
                    await SettingCommandHelper.RequireCommitAuthorizationAsync(
                        request.Scope, resolvedUserId, _tenantContext.TenantId,
                        _instanceCommitAuthority, _tenantCommitAuthority, _subjectCommitAuthority, token);
                    return await _publicationPolicyMutationBoundary.ApplyInstanceAsync(
                    new PublicationPolicyInstanceMutationRequest(
                        actorId,
                        occurredAtUtc,
                        [new PublicationPolicySettingMutation(
                            request.Key,
                            PublicationPolicyMutationKind.Set,
                            value,
                            TenantId: null,
                            IsLocked: true)]),
                    token);
                }, outerToken),
                cancellationToken);
            if (!mutationResult.Success)
            {
                string failureCode = string.IsNullOrWhiteSpace(mutationResult.FailureCode)
                    ? "event_reporting_intake_policy_invalid"
                    : mutationResult.FailureCode;
                string failureMessage = string.IsNullOrWhiteSpace(mutationResult.Message)
                    ? PublicationPolicyMutationMessages.InvalidPolicy
                    : mutationResult.Message;
                return BaseCommandResponse.Failure<Guid>(failureCode, failureMessage);
            }

            _resolver.InvalidateCache(request.Scope, scopeId);
            foreach (SettingChangedNotification notification in mutationResult.DeferredNotifications)
            {
                await _notificationHandlers.HandleAsync(notification, CancellationToken.None);
            }

            return BaseCommandResponse.Success(
                scopeId,
                $"Setting '{request.Key}' locked at {request.Scope} scope.");
        }

        var prepared = await _settingsPreparation.PrepareAsync(request, cancellationToken);
        SettingCommandHelper.RequireHumanSubject(_humanCommitScope, actorId);
        await _mutationLock.ExecuteOrderedGroupsAsync(
            [[request.Key]],
            token => _settingsPreparation.ExecutePreparedAsync(prepared, async (_, transactionToken) =>
            {
                if (request.Scope == SettingScope.Instance)
                {
                    var setting = await _systemSettings.GetByKey(request.Key, transactionToken)
                        ?? throw new InvalidOperationException($"Setting '{request.Key}' does not exist at Instance scope.");
                    setting.IsLocked = true;
                    setting.UpdatedAt = DateTime.UtcNow;
                    setting.UpdatedBy = actorId;
                    await _systemSettings.UpsertLockAsync(setting, transactionToken);
                }
                else
                {
                    await _resolver.LockAsync(request.Key, request.Scope, scopeId, actorId, transactionToken);
                }
                return true;
            }, token),
            cancellationToken);

        _resolver.InvalidateCache(request.Scope, scopeId);
        CerbosSettingsCacheInvalidation.InvalidateIfCerbosSettingChanged(
            _cerbosConfigResolver, request.Key, request.Scope, scopeId);

        var lockSource = request.Scope == SettingScope.Instance
            ? SettingSource.SystemLocked
            : SettingSource.TenantLocked;

        _logger.LogInformation(
            "Setting locked: {SettingKey} at {Scope} scope. Actor: {ActorId}",
            request.Key, request.Scope, actorId);

        await _notificationHandlers.HandleAsync(new SettingChangedNotification(
            request.Key, null, null, lockSource,
            _tenantContext.TenantId, actorId, DateTime.UtcNow), CancellationToken.None);

        return BaseCommandResponse.Success(
            scopeId,
            $"Setting '{request.Key}' locked at {request.Scope} scope.");
    }
}
