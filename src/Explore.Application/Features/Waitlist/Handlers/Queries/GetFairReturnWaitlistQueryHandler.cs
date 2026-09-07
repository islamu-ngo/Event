using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Waitlist;
using Explore.Application.DTOs.Waitlist;
using Explore.Application.Features.Waitlist.Handlers.Commands;
using Explore.Application.Features.Waitlist.Requests.Queries;
using Explore.Application.Services.Registration;
using MediatR;

namespace Explore.Application.Features.Waitlist.Handlers.Queries;

public sealed class GetFairReturnWaitlistQueryHandler(
    IFairReturnWaitlistRepository repository,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IGuestCapabilityTokenService capabilityTokens,
    IPaidCheckoutActivationService activation) :
    IRequestHandler<GetFairReturnWaitlistQuery, FairReturnWaitlistDto?>
{
    public async Task<FairReturnWaitlistDto?> Handle(
        GetFairReturnWaitlistQuery request,
        CancellationToken cancellationToken)
    {
        FairReturnWaitlistAccessContext? access =
            await repository.GetAccessAsync(
                tenantContext.TenantId,
                request.EventId,
                request.RegistrationOrderId,
                request.RegistrationOrderLineId,
                cancellationToken);
        if (access is null
            || !FairReturnWaitlistMapping.HasReadAuthority(
                access,
                currentUser.UserId,
                request.CapabilityToken,
                capabilityTokens))
        {
            return null;
        }

        bool open = (await activation.EvaluateSaleControlAsync(
            access.Order.TenantId,
            access.Order.EventId,
            cancellationToken)).IsActive;
        bool settled = access.Binding is not null
            && await repository.HasReplacementSettlementAsync(
                access.Order.TenantId,
                access.Binding.Id,
                cancellationToken);
        return FairReturnWaitlistMapping.ToDto(
            access,
            open,
            currentUser.UserId,
            settled);
    }
}
