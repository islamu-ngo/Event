using System.Collections.Generic;
using Explore.Application.DTOs.OrganizationMember;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.OrganizationMembers.Requests.Queries;

public sealed record GetMyInvitationsRequest : IQuery<List<OrganizationInvitationDto>>
{
    public required string Email { get; init; }
}
