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
using MediatR;
using Microsoft.Extensions.Logging;

public class UpdateSettingCommandHandler
    : IRequestHandler<UpdateSettingCommand, BaseCommandResponse<Guid>>
{
    private readonly IHierarchicalSettingsResolver _resolver;
    private readonly IUserPreferenceRepository _userPreferenceRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAdminContext _adminContext;
    private readonly ICerbosConfigResolver? _cerbosConfigResolver;
    private readonly IMediator _mediator;
    private readonly ILogger<UpdateSettingCommandHandler> _logger;
    private readonly ILocationPrivacyGovernanceMutationService? _locationPrivacyMutations;
    private readonly IPublicationPolicyMutationBoundary _publicationPolicyMutationBoundary;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateSettingCommandHandler(
        IHierarchicalSettingsResolver resolver,
        IUserPreferenceRepository userPreferenceRepository,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        IAdminContext adminContext,
        IMediator mediator,
        ILogger<UpdateSettingCommandHandler> logger,
        IPublicationPolicyMutationBoundary publicationPolicyMutationBoundary,
        IUnitOfWork unitOfWork,
        ICerbosConfigResolver? cerbosConfigResolver = null,
        ILocationPrivacyGovernanceMutationService? locationPrivacyMutations = null)
    {
        _resolver = resolver;
        _userPreferenceRepository = userPreferenceRepository;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _adminContext = adminContext;
        _cerbosConfigResolver = cerbosConfigResolver;
        _mediator = mediator;
        _logger = logger;
        _locationPrivacyMutations = locationPrivacyMutations;
        _publicationPolicyMutationBoundary = publicationPolicyMutationBoundary;
        _unitOfWork = unitOfWork;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(
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

        var authorityError = await SettingCommandHelper.ValidateTenantVerificationOverrideAsync(
            request.Key, request.Value, request.Scope, _resolver, cancellationToken);
        if (authorityError is not null)
        {
            return BaseCommandResponse.Validation<Guid>([authorityError], authorityError);
        }

        bool isGuardedPublicationPolicyMutation = request.Scope is SettingScope.Tenant or SettingScope.Instance
            && PublicationPolicySettingKeys.All.Contains(request.Key, StringComparer.Ordinal);

        // Check lock state
        var context = SettingCommandHelper.BuildSettingContext(
            request.Scope, _tenantContext, resolvedUserId);
        var resolved = await _resolver.ResolveWithMetadataAsync(request.Key, context, cancellationToken);

        if (!isGuardedPublicationPolicyMutation && resolved is not null)
        {
            var (isBlocked, lockReason) = SettingCommandHelper.CheckLockState(resolved, request.Scope);
            if (isBlocked)
            {
                string message = $"Cannot update '{request.Key}': {lockReason}.";
                return BaseCommandResponse.Validation<Guid>([message], message);
            }
        }

        var oldValue = resolved?.Value;
        var (scopeId, actorId) = SettingCommandHelper.GetScopeAndActorIds(
            request.Scope, _tenantContext, resolvedUserId);

        if (isGuardedPublicationPolicyMutation)
        {
            DateTime occurredAtUtc = DateTime.UtcNow;
            PublicationPolicyMutationResult mutationResult = await _unitOfWork.ExecuteInTransactionAsync(
                token => request.Scope == SettingScope.Tenant
                    ? _publicationPolicyMutationBoundary.ApplyTenantAsync(
                        new PublicationPolicyTenantMutationRequest(
                            _tenantContext.TenantId,
                            actorId,
                            occurredAtUtc,
                            [new PublicationPolicySettingMutation(
                                request.Key,
                                PublicationPolicyMutationKind.Set,
                                serializedValue,
                                _tenantContext.TenantId,
                                IsLocked: null)],
                            PublicationPolicyLockedSystemBehavior.Reject),
                        token)
                    : _publicationPolicyMutationBoundary.ApplyInstanceAsync(
                        new PublicationPolicyInstanceMutationRequest(
                            actorId,
                            occurredAtUtc,
                            [new PublicationPolicySettingMutation(
                                request.Key,
                                PublicationPolicyMutationKind.Set,
                                serializedValue,
                                TenantId: null,
                                IsLocked: resolved?.IsLocked ?? false)]),
                        token),
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

            if (mutationResult.DeferredNotifications.Length > 0)
            {
                _resolver.InvalidateCache(request.Scope, scopeId);
                foreach (SettingChangedNotification notification in mutationResult.DeferredNotifications)
                {
                    await _mediator.Publish(notification, CancellationToken.None);
                }
            }

            _logger.LogInformation(
                "Setting updated: {SettingKey} at {Scope} scope. Actor: {ActorId}",
                request.Key, request.Scope, actorId);
            return BaseCommandResponse.Success(
                scopeId,
                $"Setting '{request.Key}' updated successfully.");
        }

        // Write
        if (request.Scope == SettingScope.User)
        {
            await WriteUserPreferenceAsync(
                request.Key, serializedValue!, actorId, cancellationToken);
            _resolver.InvalidateUserCache(_tenantContext.TenantId, actorId);
        }
        else
        {
            if (_locationPrivacyMutations?.Handles(request.Key) == true)
            {
                LocationPrivacyGovernanceMutationResult mutation = await _locationPrivacyMutations.ExecuteAsync(
                    request.Key,
                    serializedValue!,
                    request.Scope,
                    request.Scope == SettingScope.Tenant ? _tenantContext.TenantId : null,
                    actorId,
                    async token =>
                    {
                        await _resolver.SetValueAsync(
                            request.Key,
                            serializedValue!,
                            request.Scope,
                            scopeId,
                            actorId,
                            token);
                        return oldValue;
                    },
                    cancellationToken);
                if (!mutation.Accepted)
                {
                    return BaseCommandResponse.Validation<Guid>([mutation.Error!], mutation.Error);
                }

                await _locationPrivacyMutations.InvalidateMutationAsync(
                    request.Scope,
                    request.Scope == SettingScope.Tenant ? _tenantContext.TenantId : null,
                    mutation.CorrectedProjections,
                    CancellationToken.None);
            }
            else
            {
                await _resolver.SetValueAsync(
                    request.Key, serializedValue!, request.Scope, scopeId, actorId, cancellationToken);
            }

            _resolver.InvalidateCache(request.Scope, scopeId);
            CerbosSettingsCacheInvalidation.InvalidateIfCerbosSettingChanged(
                _cerbosConfigResolver, request.Key, request.Scope, scopeId);
        }

        _logger.LogInformation(
            "Setting updated: {SettingKey} at {Scope} scope. Actor: {ActorId}",
            request.Key, request.Scope, actorId);

        await _mediator.Publish(new SettingChangedNotification(
            request.Key, oldValue, serializedValue,
            SettingCommandHelper.MapScopeToSource(request.Scope),
            _tenantContext.TenantId, actorId, DateTime.UtcNow), CancellationToken.None);

        return BaseCommandResponse.Success(
            scopeId,
            $"Setting '{request.Key}' updated successfully.");
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
