using System;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.OrganizationMembers.Requests.Commands;

public sealed record DeclineInvitationCommand : ICommand<BaseCommandResponse<Guid>>
{
    public Guid InvitationId { get; init; }
    public Guid UserId { get; init; }
}
