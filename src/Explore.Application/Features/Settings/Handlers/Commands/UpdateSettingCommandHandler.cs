namespace Explore.Application.Features.Settings.Handlers.Commands;

using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Logging;

public class UpdateSettingCommandHandler
    : ICommandHandler<UpdateSettingCommand, BaseCommandResponse<Guid>>
{
    private readonly IHierarchicalSettingsResolver _resolver;
    private readonly IUserPreferenceRepository _userPreferenceRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAdminContext _adminContext;
    private readonly ICerbosConfigResolver? _cerbosConfigResolver;
    private readonly IEnumerable<Contracts.Operations.INotificationHandler<SettingChangedNotification>> _notificationHandlers;
    private readonly ILogger<UpdateSettingCommandHandler> _logger;
    private readonly ILocationPrivacyGovernanceMutationService? _locationPrivacyMutations;
    private readonly IPublicationPolicyMutationBoundary _publicationPolicyMutationBoundary;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailDeliverySettingsWriter _emailSettingsWriter;
    private readonly IVisitorAccessSettingsWriter _visitorSettings;
    private readonly IEventResourceSettingsWriter _eventResourceSettingsWriter;
    private readonly IInstanceAdministratorCommitAuthority _instanceCommitAuthority;
    private readonly ITenantAdministratorCommitAuthority _tenantCommitAuthority;
    private readonly IHumanSubjectCommitAuthority _subjectCommitAuthority;
    private readonly ISettingMutationLock _mutationLock;
    private readonly IHumanCommitScopePreparation _humanCommitScope;
    private readonly IHumanSettingsMutationPreparation _settingsPreparation;

    public UpdateSettingCommandHandler(
        IHierarchicalSettingsResolver resolver,
        IUserPreferenceRepository userPreferenceRepository,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        IAdminContext adminContext,
        IEnumerable<Contracts.Operations.INotificationHandler<SettingChangedNotification>> notificationHandlers,
        ILogger<UpdateSettingCommandHandler> logger,
        IPublicationPolicyMutationBoundary publicationPolicyMutationBoundary,
        IUnitOfWork unitOfWork,
        IEmailDeliverySettingsWriter emailSettingsWriter,
        IVisitorAccessSettingsWriter visitorSettings,
        IEventResourceSettingsWriter eventResourceSettingsWriter,
        IInstanceAdministratorCommitAuthority instanceCommitAuthority,
        ITenantAdministratorCommitAuthority tenantCommitAuthority,
        IHumanSubjectCommitAuthority subjectCommitAuthority,
        ISettingMutationLock mutationLock,
        IHumanCommitScopePreparation humanCommitScope,
        IHumanSettingsMutationPreparation settingsPreparation,
        ICerbosConfigResolver? cerbosConfigResolver = null,
        ILocationPrivacyGovernanceMutationService? locationPrivacyMutations = null)
    {
        _resolver = resolver;
        _userPreferenceRepository = userPreferenceRepository;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _adminContext = adminContext;
        _cerbosConfigResolver = cerbosConfigResolver;
        _notificationHandlers = notificationHandlers;
        _logger = logger;
        _locationPrivacyMutations = locationPrivacyMutations;
        _publicationPolicyMutationBoundary = publicationPolicyMutationBoundary;
        _unitOfWork = unitOfWork;
        _emailSettingsWriter = emailSettingsWriter;
        _visitorSettings = visitorSettings;
        _eventResourceSettingsWriter = eventResourceSettingsWriter;
        _instanceCommitAuthority = instanceCommitAuthority;
        _tenantCommitAuthority = tenantCommitAuthority;
        _subjectCommitAuthority = subjectCommitAuthority;
        _mutationLock = mutationLock;
        _humanCommitScope = humanCommitScope;
        _settingsPreparation = settingsPreparation;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        UpdateSettingCommand request, CancellationToken cancellationToken)
    {
        // Validate key exists
        var definition = SettingRegistry.Get(request.Key);
        if (definition is null)
        {
            string message = $"Setting key '{request.Key}' not found in registry.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        // Validate scope range
        if (request.Scope < definition.MinScope || request.Scope > definition.MaxScope)
        {
            string message = $"Setting '{request.Key}' cannot be overridden at {request.Scope} scope. " +
                             $"Allowed range: {definition.MinScope}–{definition.MaxScope}.";
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

        if (request.Scope == SettingScope.User && resolvedUserId is null)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Unable to resolve authenticated user."],
                "Unable to resolve authenticated user.");
        }

        // Validate and serialize value
        var (isValid, serializedValue, validationError) =
            SettingCommandHelper.ValidateAndSerialize(request.Value, definition);
        if (!isValid)
        {
            return BaseCommandResponse.Validation<Guid>([validationError!], validationError);
        }

        bool isGuardedPublicationPolicyMutation = request.Scope is SettingScope.Tenant or SettingScope.Instance
            && PublicationPolicySettingKeys.All.Contains(request.Key, StringComparer.Ordinal);

        if (VisitorAccessSettingMutationGuard.Handles(request.Key))
        {
            Guid? tenantId = request.Scope == SettingScope.Tenant ? _tenantContext.TenantId : null;
            var result = await _visitorSettings.ApplyAsync(
                [new(tenantId, request.Key, VisitorAccessSettingMutationKind.SetValue, serializedValue)],
                SettingCommandHelper.RequireHumanSubject(_humanCommitScope, resolvedUserId),
                request.Scope == SettingScope.Instance ? SettingsMutationOperation.InstanceSettings : SettingsMutationOperation.TenantSettings,
                resolvedUserId, cancellationToken);
            return await result.CompleteAsync(_resolver, _notificationHandlers, request.Scope, tenantId ?? Guid.Empty);
        }

        if (EventResourceSettingMutationGuard.Handles(request.Key))
        {
            Guid? tenantId = request.Scope == SettingScope.Tenant ? _tenantContext.TenantId : null;
            EventResourceSettingsWriteResult result = await _eventResourceSettingsWriter.ApplyAsync(
                [new EventResourceSettingMutation(tenantId, request.Key,
                    EventResourceSettingMutationKind.SetValue, serializedValue)],
                SettingCommandHelper.RequireHumanSubject(_humanCommitScope, resolvedUserId),
                request.Scope == SettingScope.Instance ? SettingsMutationOperation.InstanceSettings : SettingsMutationOperation.TenantSettings,
                resolvedUserId, cancellationToken);
            return await result.CompleteAsync(
                _resolver, _notificationHandlers, request.Scope, tenantId ?? Guid.Empty);
        }

        if (EmailDeliverySettingKeys.Contains(request.Key))
        {
            Guid? targetTenantId = request.Scope == SettingScope.Tenant ? _tenantContext.TenantId : null;
            var result = await _emailSettingsWriter.ApplyAsync(
                [new EmailDeliverySettingMutation(TenantId: targetTenantId, Key: definition.Key,
                    Kind: EmailDeliverySettingMutationKind.SetValue, Value: serializedValue)],
                actorUserId: resolvedUserId, cancellationToken: cancellationToken);
            if (result.IsAccepted())
                foreach (var notification in result.ToNotifications(resolvedUserId))
                    await _notificationHandlers.HandleAsync(notification, CancellationToken.None);
            return result.ToCommandResponse(targetTenantId ?? Guid.Empty, "SMTP setting updated.");
        }

        bool isLocationPrivacyMutation = LocationPrivacySettingDefinitions.All.Any(item => item.Key == request.Key);
        if (isLocationPrivacyMutation && _locationPrivacyMutations is null)
            throw new InvalidOperationException("Location privacy settings require their governance mutation service.");
        if (!isGuardedPublicationPolicyMutation)
            return await UpdateOrdinaryAsync(request, serializedValue!, resolvedUserId, cancellationToken);
        var (scopeId, actorId) = SettingCommandHelper.GetScopeAndActorIds(
            request.Scope, _tenantContext, resolvedUserId);

        var prepared = await _settingsPreparation.PrepareAsync(request, serializedValue!, cancellationToken);
        SettingCommandHelper.RequireHumanSubject(_humanCommitScope, actorId);
        DateTime occurredAtUtc = DateTime.UtcNow;
        PublicationPolicyMutationResult mutationResult = await _mutationLock.ExecuteOrderedGroupsAsync(
            [PublicationPolicySettingKeys.All],
            outerToken => _settingsPreparation.ExecutePreparedAsync(prepared, async (_, token) =>
            {
                await SettingCommandHelper.RequireCommitAuthorizationAsync(
                    request.Scope, resolvedUserId, _tenantContext.TenantId,
                    _instanceCommitAuthority, _tenantCommitAuthority, _subjectCommitAuthority, token);
                var context = SettingCommandHelper.BuildSettingContext(request.Scope, _tenantContext, resolvedUserId);
                var resolved = await _resolver.ResolveWithMetadataAsync(request.Key, context, token);
                return request.Scope == SettingScope.Tenant
                ? await _publicationPolicyMutationBoundary.ApplyTenantAsync(
                    new PublicationPolicyTenantMutationRequest(
                        _tenantContext.TenantId, actorId, occurredAtUtc,
                        [new PublicationPolicySettingMutation(request.Key, PublicationPolicyMutationKind.Set,
                            serializedValue, _tenantContext.TenantId, IsLocked: null)],
                        PublicationPolicyLockedSystemBehavior.Reject), token)
                : await _publicationPolicyMutationBoundary.ApplyInstanceAsync(
                    new PublicationPolicyInstanceMutationRequest(actorId, occurredAtUtc,
                        [new PublicationPolicySettingMutation(request.Key, PublicationPolicyMutationKind.Set,
                            serializedValue, TenantId: null, IsLocked: resolved?.IsLocked ?? false)]), token);
            }, outerToken),
            cancellationToken);

        if (!mutationResult.Success)
        {
            string failureCode = string.IsNullOrWhiteSpace(mutationResult.FailureCode)
                ? "event_reporting_intake_policy_invalid" : mutationResult.FailureCode;
            string failureMessage = string.IsNullOrWhiteSpace(mutationResult.Message)
                ? PublicationPolicyMutationMessages.InvalidPolicy : mutationResult.Message;
            return BaseCommandResponse.Failure<Guid>(failureCode, failureMessage);
        }

        if (mutationResult.DeferredNotifications.Length > 0)
        {
            _resolver.InvalidateCache(request.Scope, scopeId);
            foreach (SettingChangedNotification notification in mutationResult.DeferredNotifications)
            {
                await _notificationHandlers.HandleAsync(notification, CancellationToken.None);
            }
        }

        _logger.LogInformation("Setting updated: {SettingKey} at {Scope} scope. Actor: {ActorId}",
            request.Key, request.Scope, actorId);
        return BaseCommandResponse.Success(scopeId, $"Setting '{request.Key}' updated successfully.");
    }

    private async Task<BaseCommandResponse<Guid>> UpdateOrdinaryAsync(
        UpdateSettingCommand request, string storedValue, Guid? resolvedUserId, CancellationToken cancellationToken)
    {
        var prepared = await _settingsPreparation.PrepareAsync(request, storedValue, cancellationToken);
        var actorId = SettingCommandHelper.RequireHumanSubject(_humanCommitScope, resolvedUserId).PrincipalId;
        var (scopeId, _) = SettingCommandHelper.GetScopeAndActorIds(request.Scope, _tenantContext, actorId);
        IReadOnlyList<LocationPrivacyProjectionIdentity> corrected = [];
        var outcome = await _mutationLock.ExecuteOrderedGroupsAsync(
            [[request.Key]],
            token => _settingsPreparation.ExecutePreparedAsync<(BaseCommandResponse<Guid> Response, SettingChangedNotification? Notification)>(
                prepared, async (_, ownerToken) =>
                {
                    var authorityError = await SettingCommandHelper.ValidateTenantVerificationOverrideAsync(
                        request.Key, request.Value, request.Scope, _resolver, ownerToken);
                    if (authorityError is not null)
                        return (BaseCommandResponse.Validation<Guid>([authorityError], authorityError), null);

                    var context = SettingCommandHelper.BuildSettingContext(request.Scope, _tenantContext, actorId);
                    var resolved = await _resolver.ResolveWithMetadataAsync(request.Key, context, ownerToken);
                    if (resolved is not null)
                    {
                        var (blocked, reason) = SettingCommandHelper.CheckLockState(resolved, request.Scope);
                        if (blocked)
                        {
                            string message = $"Cannot update '{request.Key}': {reason}.";
                            return (BaseCommandResponse.Validation<Guid>([message], message), null);
                        }
                    }

                    if (request.Scope == SettingScope.User)
                        await WriteUserPreferenceAsync(request.Key, storedValue, actorId, ownerToken);
                    else if (_locationPrivacyMutations?.Handles(request.Key) == true)
                    {
                        var location = await _locationPrivacyMutations.ExecuteAsync(request.Key, storedValue,
                            request.Scope, request.Scope == SettingScope.Tenant ? scopeId : null, actorId,
                            async persistToken =>
                            {
                                await _resolver.SetValueAsync(request.Key, storedValue, request.Scope, scopeId, actorId, persistToken);
                                return resolved?.Value;
                            }, ownerToken);
                        if (!location.Accepted)
                            return (BaseCommandResponse.Validation<Guid>([location.Error!], location.Error), null);
                        corrected = location.CorrectedProjections;
                    }
                    else
                        await _resolver.SetValueAsync(request.Key, storedValue, request.Scope, scopeId, actorId, ownerToken);
                    return (BaseCommandResponse.Success(scopeId, $"Setting '{request.Key}' updated successfully."),
                        new SettingChangedNotification(request.Key, resolved?.Value, storedValue,
                            SettingCommandHelper.MapScopeToSource(request.Scope),
                            _tenantContext.TenantId, actorId, DateTime.UtcNow));
                }, token),
            cancellationToken);

        if (outcome.Notification is not null)
        {
            if (_locationPrivacyMutations?.Handles(request.Key) == true)
                await _locationPrivacyMutations.InvalidateMutationAsync(request.Scope,
                    request.Scope == SettingScope.Tenant ? scopeId : null, corrected, CancellationToken.None);
            if (request.Scope == SettingScope.User)
                _resolver.InvalidateUserCache(_tenantContext.TenantId, actorId);
            else
            {
                _resolver.InvalidateCache(request.Scope, scopeId);
                CerbosSettingsCacheInvalidation.InvalidateIfCerbosSettingChanged(
                    _cerbosConfigResolver, request.Key, request.Scope, scopeId);
            }
            _logger.LogInformation("Setting updated: {SettingKey} at {Scope} scope. Actor: {ActorId}",
                request.Key, request.Scope, actorId);
            await _notificationHandlers.HandleAsync(outcome.Notification, CancellationToken.None);
        }
        return outcome.Response;
    }

    private async Task WriteUserPreferenceAsync(
        string key, string serializedValue, Guid userId, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        var existing = await _userPreferenceRepository.GetByUserAndKey(tenantId, userId, key);

        if (existing is not null)
        {
            existing.Value = serializedValue;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedBy = userId;
            await _userPreferenceRepository.Update(existing);
        }
        else
        {
            await _userPreferenceRepository.Create(new UserPreference
            {
                TenantId = tenantId,
                Tenant = null!,
                UserId = userId,
                SettingKey = key,
                Value = serializedValue,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId
            });
        }
    }
}
