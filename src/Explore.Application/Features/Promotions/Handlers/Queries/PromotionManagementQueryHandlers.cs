using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Promotions.Handlers.Commands;
using Explore.Application.Features.Promotions.Requests.Queries;
using Explore.Application.Features.Promotions.Validators;
using Explore.Domain;
using FluentValidation;
using MediatR;

namespace Explore.Application.Features.Promotions.Handlers.Queries;

public sealed class ListPromotionManagementQueryHandler(
    IEventRepository events,
    IPromotionManagementRepository promotions,
    ITenantContext tenant) : IRequestHandler<ListPromotionManagementQuery, IReadOnlyList<PromotionManagementDto>>
{
    public async Task<IReadOnlyList<PromotionManagementDto>> Handle(ListPromotionManagementQuery request, CancellationToken cancellationToken)
    {
        await new ListPromotionManagementQueryValidator().ValidateAndThrowAsync(request, cancellationToken);

        Event? eventTarget = await events.GetAuthorizationTargetByIdAsync(request.EventId, cancellationToken);
        if (!PromotionManagementHandlerSupport.IsPlatformManaged(eventTarget, tenant.TenantId))
        {
            return [];
        }

        IReadOnlyList<PromotionManagementEntry> entries = await promotions.ListManagementEntriesAsync(tenant.TenantId, request.EventId, request.TicketCatalogVersionId, cancellationToken);
        return entries.Select(entry => PromotionManagementMapper.Map(entry, eventTarget!)).ToArray();
    }
}

public sealed class GetPromotionManagementQueryHandler(
    IEventRepository events,
    IPromotionManagementRepository promotions,
    ITenantContext tenant) : IRequestHandler<GetPromotionManagementQuery, PromotionManagementDto?>
{
    public async Task<PromotionManagementDto?> Handle(GetPromotionManagementQuery request, CancellationToken cancellationToken)
    {
        await new GetPromotionManagementQueryValidator().ValidateAndThrowAsync(request, cancellationToken);

        Event? eventTarget = await events.GetAuthorizationTargetByIdAsync(request.EventId, cancellationToken);
        if (!PromotionManagementHandlerSupport.IsPlatformManaged(eventTarget, tenant.TenantId))
        {
            return null;
        }

        PromotionManagementEntry? entry = await promotions.GetManagementEntryAsync(tenant.TenantId, request.EventId, request.PromotionDefinitionId, cancellationToken);
        return entry is null ? null : PromotionManagementMapper.Map(entry, eventTarget!);
    }
}
