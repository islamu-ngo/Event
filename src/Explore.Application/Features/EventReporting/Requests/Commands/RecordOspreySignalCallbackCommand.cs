using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventReporting.Requests.Commands;

public sealed record RecordOspreySignalCallbackCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required OspreySignalCallbackRequestDto Request { get; init; }
}
