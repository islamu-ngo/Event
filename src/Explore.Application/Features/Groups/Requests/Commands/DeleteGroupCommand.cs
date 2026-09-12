using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Groups.Requests.Commands;

public sealed record DeleteGroupCommand : ICommand<BaseCommandResponse<Guid>>
{
    public Guid Id { get; init; }
    public required string UserId { get; init; }
}
