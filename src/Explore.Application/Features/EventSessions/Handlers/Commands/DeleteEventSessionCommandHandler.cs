using System;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Operations;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Exceptions;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventSessions.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventSessions.Handlers.Commands;

public class DeleteEventSessionCommandHandler : ICommandHandler<DeleteEventSessionCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventSessionRepository _eventSessionRepository;
    private readonly IEventTicketCatalogRepository _catalogs;
    private readonly IUnitOfWork _unitOfWork;
    private readonly EventLocationAttachmentService _eventLocationAttachmentService;
    private readonly ITenantContext _tenantContext;
    private readonly IUserContext _userContext;
    private readonly IAdminContext _adminContext;
    private readonly IMachinePrincipalAccessor _machinePrincipal;
    private readonly IMachineMutationCommitAuthority _machineAuthority;
    private readonly RequestAuthorization<DeleteEventSessionCommand> _authorization;
    private readonly ILogger<DeleteEventSessionCommandHandler> _logger;

    public DeleteEventSessionCommandHandler(
        IEventSessionRepository eventSessionRepository,
        IEventTicketCatalogRepository catalogs,
        IUnitOfWork unitOfWork,
        EventLocationAttachmentService eventLocationAttachmentService,
        ITenantContext tenantContext, IUserContext userContext, IAdminContext adminContext,
        RequestAuthorization<DeleteEventSessionCommand> authorization,
        IMachinePrincipalAccessor machinePrincipal,
        IMachineMutationCommitAuthority machineAuthority,
        ILogger<DeleteEventSessionCommandHandler> logger)
    {
        _eventSessionRepository = eventSessionRepository;
        _catalogs = catalogs;
        _unitOfWork = unitOfWork;
        _eventLocationAttachmentService = eventLocationAttachmentService;
        _tenantContext = tenantContext;
        _userContext = userContext;
        _adminContext = adminContext;
        _machinePrincipal = machinePrincipal;
        _machineAuthority = machineAuthority;
        _authorization = authorization;
        _logger = logger;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(DeleteEventSessionCommand request, CancellationToken cancellationToken)
    {
        Guid? userId = await _adminContext.ResolveUserIdAsync(cancellationToken);
        bool machine = _machinePrincipal.IsMachineCaller;
        if (!_userContext.IsAuthenticated || _tenantContext.TenantId == Guid.Empty || !machine && userId is null)
            return BaseCommandResponse.Authentication<Guid>();

        DeleteResult result = await _unitOfWork.ExecuteSerializableAsync(async token =>
        {
            var eventSession = await _eventSessionRepository.GetById(request.Id);
            if (eventSession is null)
                return DeleteResult.NotFound;
            await _authorization.AuthorizeAsync(request, _logger, token);
            var parent = machine
                ? await _machineAuthority.GetAuthorizedSessionParentAsync(
                    _tenantContext.TenantId, eventSession.EventId, AuthorizationActions.Delete, token)
                : userId is { } humanId
                    ? await _eventSessionRepository.GetAuthorizedParentForMutationAsync(
                        _tenantContext.TenantId, humanId, eventSession.EventId, AuthorizationActions.Delete, token)
                    : null;
            if (parent is null)
                throw new AuthorizationException(ResourceKinds.EventSession, AuthorizationActions.Delete);
            await _catalogs.GetDraftCatalogForUpdateAsync(eventSession.EventId, eventSession.TenantId, token);
            EventTicketCatalogVersion? published = await _catalogs.GetPublishedForUpdateAsync(
                eventSession.EventId,
                eventSession.TenantId,
                token);
            EventSession? lockedSession = await _eventSessionRepository.GetForMutationAsync(eventSession.Id, token);
            if (lockedSession is null)
            {
                return DeleteResult.NotFound;
            }
            if (lockedSession.EventId != eventSession.EventId || lockedSession.TenantId != _tenantContext.TenantId)
                throw new AuthorizationException(ResourceKinds.EventSession, AuthorizationActions.Delete);

            if (ReferencesEventSession(published, lockedSession.Id))
            {
                return DeleteResult.Referenced;
            }

            Guid? eventLocationId = lockedSession.EventLocationId;
            if (machine && await _machineAuthority.GetAuthorizedSessionParentAsync(
                    _tenantContext.TenantId, eventSession.EventId, AuthorizationActions.Delete, token) is null)
                throw new AuthorizationException(ResourceKinds.EventSession, AuthorizationActions.Delete);
            lockedSession.DetachEventLocationForDeletion();
            await _eventSessionRepository.Delete(lockedSession);
            await _eventLocationAttachmentService.DetachIfUnreferencedAsync(
                eventLocationId,
                token);
            if (machine && await _machineAuthority.GetAuthorizedSessionParentAsync(
                    _tenantContext.TenantId, eventSession.EventId, AuthorizationActions.Delete, token) is null)
                throw new AuthorizationException(ResourceKinds.EventSession, AuthorizationActions.Delete);
            return DeleteResult.Deleted;
        }, cancellationToken);

        return result switch
        {
            DeleteResult.Deleted => Success(request.Id),
            DeleteResult.Referenced => Conflict(request.Id),
            _ => NotFound(request.Id)
        };
    }

    private static bool ReferencesEventSession(EventTicketCatalogVersion? catalog, Guid eventSessionId) =>
        catalog?.TicketTypes
            .Where(ticketType => !ticketType.IsDeleted)
            .SelectMany(ticketType => ticketType.Entitlements)
            .Any(entitlement => entitlement.EventSessionId == eventSessionId) == true;

    private static BaseCommandResponse<Guid> Success(Guid id) =>
        BaseCommandResponse.Success(id, "Event session deleted successfully.");

    private static BaseCommandResponse<Guid> NotFound(Guid id) =>
        BaseCommandResponse.Failure<Guid>(
            "event_session_not_found",
            "Event session not found.",
            id: id);

    private static BaseCommandResponse<Guid> Conflict(Guid id) =>
        BaseCommandResponse.Failure<Guid>(
            "event_session_ticket_entitlement_conflict",
            "Event session is referenced by a published ticket catalog.",
            id: id);

    private enum DeleteResult
    {
        Deleted,
        NotFound,
        Referenced
    }
}
