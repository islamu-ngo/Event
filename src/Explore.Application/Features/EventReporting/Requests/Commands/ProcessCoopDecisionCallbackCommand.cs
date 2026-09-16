using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventReporting.Requests.Commands;

public sealed record ProcessCoopDecisionCallbackCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required CoopDecisionCallbackRequestDto Request { get; init; }
}
