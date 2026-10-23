using Explore.Application.Contracts.Persistence;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventAgendaItems.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventAgendaItems.Handlers.Commands;

public class DeleteEventAgendaItemCommandHandler : ICommandHandler<DeleteEventAgendaItemCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventAgendaItemRepository _eventAgendaItemRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly EventLocationAttachmentService _eventLocationAttachmentService;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUser;
    private readonly RequestAuthorization<DeleteEventAgendaItemCommand> _authorization;
    private readonly ILogger<DeleteEventAgendaItemCommandHandler> _logger;

    public DeleteEventAgendaItemCommandHandler(
        IEventAgendaItemRepository eventAgendaItemRepository,
        IUnitOfWork unitOfWork,
        EventLocationAttachmentService eventLocationAttachmentService,
        ITenantContext tenantContext,
        ICurrentUserService currentUser,
        RequestAuthorization<DeleteEventAgendaItemCommand> authorization,
        ILogger<DeleteEventAgendaItemCommandHandler> logger)
    {
        _eventAgendaItemRepository = eventAgendaItemRepository;
        _unitOfWork = unitOfWork;
        _eventLocationAttachmentService = eventLocationAttachmentService;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _authorization = authorization;
        _logger = logger;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(DeleteEventAgendaItemCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } userId)
            return BaseCommandResponse.Authentication<Guid>();
        return await _unitOfWork.ExecuteSerializableAsync(async token =>
        {
            var candidate = await _eventAgendaItemRepository.GetById(request.Id);
            if (candidate is null)
                return BaseCommandResponse.Validation<Guid>(["Event agenda item not found."], "Event agenda item not found.");
            await _authorization.AuthorizeAsync(request, _logger, token);
            if (await _eventAgendaItemRepository.GetAuthorizedParentForMutationAsync(
                    _tenantContext.TenantId, userId, candidate.EventId, AuthorizationActions.Delete, token) is null)
                throw new AuthorizationException(ResourceKinds.EventAgendaItem, AuthorizationActions.Delete);
            var agendaItem = await _eventAgendaItemRepository.GetForMutationAsync(request.Id, token);
            if (agendaItem is null || agendaItem.EventId != candidate.EventId || agendaItem.TenantId != _tenantContext.TenantId)
                throw new AuthorizationException(ResourceKinds.EventAgendaItem, AuthorizationActions.Delete);
            Guid? eventLocationId = agendaItem.EventLocationId;
            agendaItem.DetachEventLocationForDeletion();
            await _eventAgendaItemRepository.Delete(agendaItem);
            await _eventLocationAttachmentService.DetachIfUnreferencedAsync(
                eventLocationId,
                token);
            token.ThrowIfCancellationRequested();
            return BaseCommandResponse.Success(agendaItem.Id, "Event agenda item deleted successfully.");
        }, cancellationToken);
    }
}
