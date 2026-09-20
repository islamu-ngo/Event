using System.Collections.Generic;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Organization;

namespace Explore.Application.Features.Users.Requests.Queries;

/// <summary>
/// Request to get all organizations a user is a member of.
/// </summary>
public sealed record GetUserOrganizationsRequest(Guid UserId = default) : IQuery<List<OrganizationListDto>>;
