using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Promotions.Handlers.Commands;
using Explore.Application.Features.Promotions.Requests.Queries;
using Explore.Application.Features.Promotions.Validators;
using Explore.Domain;
using FluentValidation;

namespace Explore.Application.Features.Promotions.Handlers.Queries;

public sealed class ListPromotionManagementQueryHandler(
    IEventRepository events,
    IPromotionManagementRepository promotions,
    ITenantContext tenant) : IQueryHandler<ListPromotionManagementQuery, IReadOnlyList<PromotionManagementDto>>
{
    public async Task<IReadOnlyList<PromotionManagementDto>> QueryAsync(ListPromotionManagementQuery query, CancellationToken cancellationToken)
    {
        await new ListPromotionManagementQueryValidator().ValidateAndThrowAsync(query, cancellationToken);

        Event? eventTarget = await events.GetAuthorizationTargetByIdAsync(query.EventId, cancellationToken);
        if (!PromotionManagementHandlerSupport.IsPlatformManaged(eventTarget, tenant.TenantId))
        {
            return [];
        }

        IReadOnlyList<PromotionManagementEntry> entries = await promotions.ListManagementEntriesAsync(tenant.TenantId, query.EventId, query.TicketCatalogVersionId, cancellationToken);
        return entries.Select(entry => PromotionManagementMapper.Map(entry, eventTarget!)).ToArray();
    }
}

public sealed class GetPromotionManagementQueryHandler(
    IEventRepository events,
    IPromotionManagementRepository promotions,
    ITenantContext tenant) : IQueryHandler<GetPromotionManagementQuery, PromotionManagementDto?>
{
    public async Task<PromotionManagementDto?> QueryAsync(GetPromotionManagementQuery query, CancellationToken cancellationToken)
    {
        await new GetPromotionManagementQueryValidator().ValidateAndThrowAsync(query, cancellationToken);

        Event? eventTarget = await events.GetAuthorizationTargetByIdAsync(query.EventId, cancellationToken);
        if (!PromotionManagementHandlerSupport.IsPlatformManaged(eventTarget, tenant.TenantId))
        {
            return null;
        }

        PromotionManagementEntry? entry = await promotions.GetManagementEntryAsync(tenant.TenantId, query.EventId, query.PromotionDefinitionId, cancellationToken);
        return entry is null ? null : PromotionManagementMapper.Map(entry, eventTarget!);
    }
}

