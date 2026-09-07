// ABOUTME: Command handler for removing a setting override, reverting to parent scope cascade.
// ABOUTME: User scope uses IUserPreferenceRepository; Tenant/Org/Group uses resolver.RemoveOverrideAsync.

namespace Explore.Application.Features.Settings.Handlers.Commands;

using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain.Settings;
using MediatR;
using Microsoft.Extensions.Logging;

public class ResetSettingCommandHandler
    : IRequestHandler<ResetSettingCommand, BaseCommandResponse<Guid>>
{
    private readonly IHierarchicalSettingsResolver _resolver;
    private readonly IUserPreferenceRepository _userPreferenceRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAdminContext _adminContext;
    private readonly ICerbosConfigResolver? _cerbosConfigResolver;
    private readonly IMediator _mediator;
    private readonly ILogger<ResetSettingCommandHandler> _logger;
    private readonly ILocationPrivacyGovernanceMutationService? _locationPrivacyMutations;
    private readonly IPublicationPolicyMutationBoundary _publicationPolicyMutationBoundary;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailDeliverySettingsWriter _emailSettingsWriter;

    public ResetSettingCommandHandler(
        IHierarchicalSettingsResolver resolver,
        IUserPreferenceRepository userPreferenceRepository,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        IAdminContext adminContext,
        IMediator mediator,
        ILogger<ResetSettingCommandHandler> logger,
        IPublicationPolicyMutationBoundary publicationPolicyMutationBoundary,
        IUnitOfWork unitOfWork,
        IEmailDeliverySettingsWriter emailSettingsWriter,
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
        _emailSettingsWriter = emailSettingsWriter;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(
        ResetSettingCommand request, CancellationToken cancellationToken)
    {
        // Validate key exists
        var definition = SettingRegistry.Get(request.Key);
        if (definition is null)
        {
            string message = $"Setting key '{request.Key}' not found in registry.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        // Instance scope cannot be reset (it IS the root)
        if (request.Scope == SettingScope.Instance)
        {
            const string message = "Cannot reset instance-level settings. Use update to change the value.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        if (request.Scope < definition.MinScope || request.Scope > definition.MaxScope)
        {
            string message = $"Setting '{request.Key}' cannot be reset at {request.Scope} scope.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        // Authorization
        var (authorized, authError) = await SettingCommandHelper.CheckAuthorizationAsync(
            request.Scope, _adminContext, _tenantContext, _currentUserService, cancellationToken);
        if (!authorized)
        {
            return BaseCommandResponse.Validation<Guid>([authError!], authError);
        }

        if (EmailDeliverySettingKeys.Contains(request.Key))
        {
            Guid? actor = await SettingCommandHelper.ResolveCurrentUserIdAsync(_adminContext, _currentUserService, cancellationToken);
            var result = await _emailSettingsWriter.ApplyAsync(
                [new EmailDeliverySettingMutation(TenantId: _tenantContext.TenantId, Key: definition.Key,
                    Kind: EmailDeliverySettingMutationKind.Remove)],
                actorUserId: actor, cancellationToken: cancellationToken);
            if (result.IsAccepted())
                foreach (var notification in result.ToNotifications(actor))
                    await _mediator.Publish(notification, CancellationToken.None);
            return result.ToCommandResponse(_tenantContext.TenantId, "SMTP override reset.");
        }

        // Get current value for notification
        var context = SettingCommandHelper.BuildSettingContext(
            request.Scope, _tenantContext, _currentUserService);
        var resolved = await _resolver.ResolveWithMetadataAsync(
            request.Key, context, cancellationToken);
        var oldValue = resolved?.Value;

        var (scopeId, actorId) = SettingCommandHelper.GetScopeAndActorIds(
            request.Scope, _tenantContext, _currentUserService);

        bool isGuardedPublicationPolicyMutation = request.Scope == SettingScope.Tenant
            && PublicationPolicySettingKeys.All.Contains(request.Key, StringComparer.Ordinal);
        if (isGuardedPublicationPolicyMutation)
        {
            DateTime occurredAtUtc = DateTime.UtcNow;
            PublicationPolicyMutationResult mutationResult = await _unitOfWork.ExecuteInTransactionAsync(
                token => _publicationPolicyMutationBoundary.ApplyTenantAsync(
                    new PublicationPolicyTenantMutationRequest(
                        _tenantContext.TenantId,
                        actorId,
                        occurredAtUtc,
                        [new PublicationPolicySettingMutation(
                            request.Key,
                            PublicationPolicyMutationKind.Remove,
                            JsonValue: null,
                            _tenantContext.TenantId,
                            IsLocked: null)],
                        PublicationPolicyLockedSystemBehavior.RemoveOverride),
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

            _resolver.InvalidateCache(request.Scope, scopeId);
            foreach (SettingChangedNotification notification in mutationResult.DeferredNotifications)
            {
                await _mediator.Publish(notification, CancellationToken.None);
            }

            _logger.LogInformation(
                "Setting reset: {SettingKey} at {Scope} scope. Actor: {ActorId}",
                request.Key, request.Scope, actorId);
            return BaseCommandResponse.Success(
                scopeId,
                $"Setting '{request.Key}' reset to inherited value.");
        }

        // Remove override
        if (request.Scope == SettingScope.User)
        {
            var removed = await _userPreferenceRepository.RemoveOverride(
                _tenantContext.TenantId, actorId, request.Key);
            if (!removed)
            {
                string message = $"No user override found for '{request.Key}'.";
                return BaseCommandResponse.Validation<Guid>([message], message);
            }

            _resolver.InvalidateUserCache(_tenantContext.TenantId, actorId);
        }
        else
        {
            try
            {
                await _resolver.RemoveOverrideAsync(
                    request.Key, request.Scope, scopeId, actorId, cancellationToken);
            }
            catch (SettingSystemLockedException exception)
            {
                return BaseCommandResponse.Failure<Guid>(
                    SettingSystemLockedException.Code,
                    exception.Message);
            }

            _resolver.InvalidateCache(request.Scope, scopeId);
            CerbosSettingsCacheInvalidation.InvalidateIfCerbosSettingChanged(
                _cerbosConfigResolver, request.Key, request.Scope, scopeId);
            if (_locationPrivacyMutations?.Handles(request.Key) == true)
            {
                await _locationPrivacyMutations.InvalidateScopeAsync(
                    request.Scope,
                    request.Scope == SettingScope.Tenant ? _tenantContext.TenantId : null,
                    CancellationToken.None);
            }
        }

        _logger.LogInformation(
            "Setting reset: {SettingKey} at {Scope} scope. Actor: {ActorId}",
            request.Key, request.Scope, actorId);

        await _mediator.Publish(new SettingChangedNotification(
            request.Key, oldValue, null,
            SettingCommandHelper.MapScopeToSource(request.Scope),
            _tenantContext.TenantId, actorId, DateTime.UtcNow), CancellationToken.None);

        return BaseCommandResponse.Success(
            scopeId,
            $"Setting '{request.Key}' reset to inherited value.");
    }
}
