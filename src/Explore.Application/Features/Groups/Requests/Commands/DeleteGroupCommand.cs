using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Groups.Requests.Commands;

public sealed record DeleteGroupCommand : IRequest<BaseCommandResponse<Guid>>
{
    public Guid Id { get; init; }
    public required string UserId { get; init; }
}
