using System;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.OrganizationMembers.Requests.Commands;

public sealed record DeclineInvitationCommand : IRequest<BaseCommandResponse<Guid>>
{
    public Guid InvitationId { get; init; }
    public Guid UserId { get; init; }
}
