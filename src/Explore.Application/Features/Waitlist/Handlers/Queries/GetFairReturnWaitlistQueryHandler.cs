using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Waitlist;
using Explore.Application.DTOs.Waitlist;
using Explore.Application.Features.Waitlist.Handlers.Commands;
using Explore.Application.Features.Waitlist.Requests.Queries;
using Explore.Application.Services.Registration;

namespace Explore.Application.Features.Waitlist.Handlers.Queries;

public sealed class GetFairReturnWaitlistQueryHandler(
    IRegistrationPrivateReader reader,
    IPaidCheckoutActivationService activation) :
    IQueryHandler<GetFairReturnWaitlistQuery, FairReturnWaitlistDto?>
{
    public async Task<FairReturnWaitlistDto?> QueryAsync(
        GetFairReturnWaitlistQuery query,
        CancellationToken cancellationToken)
    {
        var graph = await reader.ReadAsync(new(RegistrationPrivateReadKind.Waitlist,
            query.EventId, query.RegistrationOrderId, query.RegistrationOrderLineId),
            query.CapabilityToken, cancellationToken);
        FairReturnWaitlistAccessContext? access = graph?.Waitlist;
        if (access is null)
        {
            return null;
        }

        bool open = (await activation.EvaluateSaleControlAsync(
            access.Order.TenantId,
            access.Order.EventId,
            graph!.TenantSaleControl,
            graph.EventSaleControl,
            cancellationToken)).IsActive;
        return FairReturnWaitlistMapping.ToDto(
            access,
            open,
            graph.PrincipalId,
            graph.ReplacementSettled);
    }
}
