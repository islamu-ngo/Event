using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain.Federation;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Events.Handlers.Queries;

public class GetMyEventsRequestHandler : IQueryHandler<GetMyEventsRequest, PaginatedResult<EventListDto>>
{
    private readonly IEventRepository _eventRepository;
    private readonly IObjectStorageService _objectStorageService;
    private readonly ILogger<GetMyEventsRequestHandler> _logger;
    private readonly IPdsSyncOutboxRepository _outboxRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IPrivateEventManagementReader? _privateManagement;

    public GetMyEventsRequestHandler(
        IEventRepository eventRepository,
        IObjectStorageService objectStorageService,
        ILogger<GetMyEventsRequestHandler> logger,
        IPdsSyncOutboxRepository outboxRepository,
        ITenantContext tenantContext,
        IPrivateEventManagementReader? privateManagement = null)
    {
        _eventRepository = eventRepository;
        _objectStorageService = objectStorageService;
        _logger = logger;
        _outboxRepository = outboxRepository;
        _tenantContext = tenantContext;
        _privateManagement = privateManagement;
    }

    public async Task<PaginatedResult<EventListDto>> QueryAsync(GetMyEventsRequest request, CancellationToken cancellationToken)
    {
        PrivateEventManagementGraph? graph = null;
        if (_privateManagement is not null)
        {
            if (!Guid.TryParse(request.UserId, out Guid userId) || userId == Guid.Empty)
                throw new UnauthorizedAccessException("My-events requires the original human identifier.");
            graph = await _privateManagement.ReadAsync(new(PrivateEventManagementReadKind.MyEvents,
                PageNumber: request.PageNumber, PageSize: request.PageSize, ExpectedUserId: userId), cancellationToken);
        }
        var (events, totalCount) = graph is null
            ? await _eventRepository.GetMyEventsWithDetailsPaged(request.UserId, request.PageNumber, request.PageSize)
            : (graph.Events, graph.TotalCount);
        var eventDtos = events.Select(EventMapper.ToListItem).ToList();
        IReadOnlyList<PdsSyncOutbox> deliveryRows = graph is not null ? graph.DeliveryStates : await _outboxRepository.GetCurrentEventDeliveryStatesAsync(
            _tenantContext.TenantId,
            eventDtos.Select(dto => dto.Id).ToArray(),
            cancellationToken);
        Dictionary<Guid, PdsSyncOutbox> deliveryByEvent = deliveryRows
            .GroupBy(row => row.SourceEntityId)
            .ToDictionary(group => group.Key, group => group.First());

        // Resolve presigned URLs for images
        foreach (var dto in eventDtos)
        {
            if (deliveryByEvent.TryGetValue(dto.Id, out PdsSyncOutbox? delivery))
            {
                dto.AtprotoDeliveryStatus = DeliveryStatus(delivery);
                dto.AtprotoDeliveryFailureCode = delivery.LastError;
            }
            else if (dto.AtprotoRecordId.HasValue)
            {
                dto.AtprotoDeliveryStatus = "published";
            }
            dto.FeaturedImageUri = await ResolveImageUrl(dto.FeaturedImageUri);
            dto.ActorProfilePictureUri = await ResolveImageUrl(dto.ActorProfilePictureUri);
        }

        return PaginatedResult<EventListDto>.Create(eventDtos, totalCount, request.PageNumber, request.PageSize);
    }

    private Task<string?> ResolveImageUrl(string? objectKeyOrUri)
        => StoragePresentationUrlResolver.ResolveImageUrlAsync(
            objectKeyOrUri,
            _logger,
            "my events image");

    private static string DeliveryStatus(PdsSyncOutbox delivery) => delivery.Status switch
    {
        PdsSyncStatus.Pending when delivery.RetryCount > 0 => "retrying",
        PdsSyncStatus.Pending => "pending",
        PdsSyncStatus.Processing => "publishing",
        PdsSyncStatus.Completed when delivery.Operation == PdsSyncOperation.Delete => "removed",
        PdsSyncStatus.Completed => "published",
        PdsSyncStatus.Failed or PdsSyncStatus.DeadLettered => "failed",
        PdsSyncStatus.Superseded => "superseded",
        _ => "pending"
    };
}
