using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Caching;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;
using Explore.Domain.Federation;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Events.Handlers.Commands;

/// <summary>
/// Holds current event-delete authority, paid-evidence checks, session cascade and
/// federation outbox work in one caller-owned Serializable transaction.
/// </summary>
public class DeleteEventCommandHandler : ICommandHandler<DeleteEventCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventRepository _eventRepository;
    private readonly IEventSessionRepository _eventSessionRepository;
    private readonly IRegistrationInventoryRepository _registrationInventoryRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<DeleteEventCommandHandler> _logger;
    private readonly HybridCache _cache;
    private readonly IUnitOfWork _unitOfWork;
    private readonly AtprotoEventPublicationPlanner _atprotoPublicationPlanner;
    private readonly ITenantContext _tenantContext;
    private readonly IAdminContext _adminContext;
    private readonly RequestAuthorization<DeleteEventCommand> _authorization;

    public DeleteEventCommandHandler(
        IEventRepository eventRepository,
        IEventSessionRepository eventSessionRepository,
        IRegistrationInventoryRepository registrationInventoryRepository,
        ICurrentUserService currentUserService,
        ILogger<DeleteEventCommandHandler> logger,
        HybridCache cache,
        IUnitOfWork unitOfWork,
        AtprotoEventPublicationPlanner atprotoPublicationPlanner,
        ITenantContext tenantContext,
        IAdminContext adminContext,
        RequestAuthorization<DeleteEventCommand> authorization)
    {
        _eventRepository = eventRepository;
        _eventSessionRepository = eventSessionRepository;
        _registrationInventoryRepository = registrationInventoryRepository;
        _currentUserService = currentUserService;
        _logger = logger;
        _cache = cache;
        _unitOfWork = unitOfWork;
        _atprotoPublicationPlanner = atprotoPublicationPlanner;
        _tenantContext = tenantContext;
        _adminContext = adminContext;
        _authorization = authorization;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(DeleteEventCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated
            || await _adminContext.ResolveUserIdAsync(cancellationToken) is not { } userId)
        {
            _logger.LogWarning("Delete event failed: User ID not found in authentication context");
            return BaseCommandResponse.Authentication<Guid>("An authenticated user is required to delete an event.");
        }
        Guid tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
            return Denied(request.Id);
        var federationOutboxId = Guid.CreateVersion7();
        var federationCreatedAt = DateTime.UtcNow;
        BaseCommandResponse<Guid> response;
        try
        {
            response = await _unitOfWork.ExecuteSerializableAsync(async token =>
            {
                if (await _eventRepository.GetById(request.Id) is null)
                {
                    _logger.LogWarning("Delete event failed because the event was not found");
                    return BaseCommandResponse.NotFound<Guid>("Event not found.", request.Id);
                }
                await _authorization.AuthorizeAsync(request, _logger, token);
                var current = await _eventRepository.GetAuthorizedForDeleteAsync(tenantId, userId, request.Id, token);
                if (current is null)
                    throw new AuthorizationException(ResourceKinds.Event, AuthorizationActions.Delete);
                if (await _registrationInventoryRepository.HasPaidEvidenceAsync(current.Id, tenantId, token))
                {
                    _logger.LogWarning("Delete event rejected because paid evidence exists");
                    return BaseCommandResponse.Failure<Guid>(
                        DeleteEventFailureCodes.PaidEvidenceConflict, "An event with paid evidence cannot be deleted.", id: request.Id);
                }
                var sessions = await _eventSessionRepository.GetSessionsByEvent(current.Id, token);
                _logger.LogInformation("Cascading soft delete: Found {SessionCount} event session(s)", sessions.Count);
                foreach (var candidate in sessions.OrderBy(session => session.Id))
                {
                    var session = await _eventSessionRepository.GetForMutationAsync(candidate.Id, token);
                    if (session is null || session.EventId != current.Id || session.TenantId != tenantId)
                        throw new AuthorizationException(ResourceKinds.Event, AuthorizationActions.Delete);
                    _logger.LogDebug("Soft deleting an event session");
                    await _eventSessionRepository.Delete(session);
                    token.ThrowIfCancellationRequested();
                }
                await _eventRepository.Delete(current);
                await _atprotoPublicationPlanner.PlanEventAsync(
                    new AtprotoEventPublicationInput(
                        tenantId, userId, current.Id, current.ConcurrencyStamp,
                        PdsSyncOperation.Delete, federationOutboxId, federationCreatedAt), token);
                token.ThrowIfCancellationRequested();
                return BaseCommandResponse.Success(request.Id);
            }, cancellationToken);
        }
        catch (AuthorizationException exception) when (exception is not AuthorizationProviderUnavailableException)
        {
            return Denied(request.Id);
        }
        if (!response.IsSuccess)
            return response;
        _logger.LogInformation("Event deletion committed");

        try
        {
            await _cache.RemoveByTagAsync(CacheTags.Event(request.Id), cancellationToken);
            await _cache.RemoveByTagAsync(CacheTags.EventListByTenant(tenantId), cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("Cache invalidation failed after event deletion");
        }

        return response;
    }

    private BaseCommandResponse<Guid> Denied(Guid id)
    {
        _logger.LogWarning("Delete event failed because current deletion authority was denied");
        return BaseCommandResponse.Failure<Guid>(
            DeleteEventFailureCodes.AuthorityDenied, "Current event deletion authority is required.", id: id);
    }
}
