using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Responses;

namespace Explore.Application.Contracts.Services;

public interface IRegistrationOrderStarter
{
    Task<BaseCommandResponse<Guid>> StartAsync(
        CreateRegistrationOrderWithHoldCommand request,
        CancellationToken cancellationToken);
}
