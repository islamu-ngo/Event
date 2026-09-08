using Explore.Application.DTOs.EventReporting;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventReporting.Requests.Commands;

public sealed record ProcessCoopDecisionCallbackCommand : IRequest<BaseCommandResponse<Guid>>
{
    public required CoopDecisionCallbackRequestDto Request { get; init; }
}
