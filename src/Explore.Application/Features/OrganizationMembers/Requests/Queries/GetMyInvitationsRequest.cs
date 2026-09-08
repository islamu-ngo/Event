using System.Collections.Generic;
using Explore.Application.DTOs.OrganizationMember;
using MediatR;

namespace Explore.Application.Features.OrganizationMembers.Requests.Queries;

public sealed record GetMyInvitationsRequest : IRequest<List<OrganizationInvitationDto>>
{
    public required string Email { get; init; }
}
