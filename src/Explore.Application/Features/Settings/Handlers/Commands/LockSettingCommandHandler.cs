namespace Explore.Application.Features.Settings.Handlers.Commands;

using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain.Settings;
using MediatR;
using Microsoft.Extensions.Logging;

public class LockSettingCommandHandler
    : IRequestHandler<LockSettingCommand, BaseCommandResponse<Guid>>
{
    private readonly IHierarchicalSettingsResolver _resolver;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAdminContext _adminContext;
    private readonly ICerbosConfigResolver? _cerbosConfigResolver;
    private readonly IMediator _mediator;
    private readonly ILogger<LockSettingCommandHandler> _logger;
    private readonly IPublicationPolicyMutationBoundary _publicationPolicyMutationBoundary;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISettingMutationLock _mutationLock;

    public LockSettingCommandHandler(
        IHierarchicalSettingsResolver resolver,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        IAdminContext adminContext,
        IMediator mediator,
        ILogger<LockSettingCommandHandler> logger,
        IPublicationPolicyMutationBoundary publicationPolicyMutationBoundary,
        IUnitOfWork unitOfWork,
        ISettingMutationLock mutationLock,
        ICerbosConfigResolver? cerbosConfigResolver = null)
    {
        _resolver = resolver;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _adminContext = adminContext;
        _cerbosConfigResolver = cerbosConfigResolver;
        _mediator = mediator;
        _logger = logger;
        _publicationPolicyMutationBoundary = publicationPolicyMutationBoundary;
        _unitOfWork = unitOfWork;
        _mutationLock = mutationLock;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(
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

        var (scopeId, actorId) = SettingCommandHelper.GetScopeAndActorIds(
            request.Scope, _tenantContext, _currentUserService);

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
            var context = SettingCommandHelper.BuildSettingContext(
                request.Scope, _tenantContext, _currentUserService);
            ResolvedSetting? resolved = await _resolver.ResolveWithMetadataAsync(
                request.Key, context, cancellationToken);
            string value = resolved?.Value ?? definition.DefaultValue;
            DateTime occurredAtUtc = DateTime.UtcNow;
            PublicationPolicyMutationResult mutationResult = await _unitOfWork.ExecuteInTransactionAsync(
                token => _publicationPolicyMutationBoundary.ApplyInstanceAsync(
                    new PublicationPolicyInstanceMutationRequest(
                        actorId,
                        occurredAtUtc,
                        [new PublicationPolicySettingMutation(
                            request.Key,
                            PublicationPolicyMutationKind.Set,
                            value,
                            TenantId: null,
                            IsLocked: true)]),
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

            return BaseCommandResponse.Success(
                scopeId,
                $"Setting '{request.Key}' locked at {request.Scope} scope.");
        }

        await _mutationLock.ExecuteAsync(
            request.Key,
            async token =>
            {
                await _resolver.LockAsync(
                    request.Key,
                    request.Scope,
                    scopeId,
                    actorId,
                    token);
                return true;
            },
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

        await _mediator.Publish(new SettingChangedNotification(
            request.Key, null, null, lockSource,
            _tenantContext.TenantId, actorId, DateTime.UtcNow), CancellationToken.None);

        return BaseCommandResponse.Success(
            scopeId,
            $"Setting '{request.Key}' locked at {request.Scope} scope.");
    }
}
