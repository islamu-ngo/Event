using Explore.Application.DTOs.EventReporting;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventReporting.Requests.Commands;

public sealed record RecordOspreySignalCallbackCommand : IRequest<BaseCommandResponse<Guid>>
{
    public required OspreySignalCallbackRequestDto Request { get; init; }
}
