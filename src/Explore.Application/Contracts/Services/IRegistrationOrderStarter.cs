using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Responses;

namespace Explore.Application.Contracts.Services;

public interface IRegistrationOrderStarter
{
    Task<BaseCommandResponse<Guid>> StartAsync(
        CreateRegistrationOrderWithHoldCommand request,
        CancellationToken cancellationToken);

    // An absent committed match is not permission to allocate or reclaim a live HTTP idempotency owner.
    Task<BaseCommandResponse<Guid>?> TryRecoverCommittedGuestAsync(
        StartGuestRegistrationOrderCommand request,
        CancellationToken cancellationToken);
}
