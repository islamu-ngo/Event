using Explore.Application.DTOs.OrganizationPosition;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.OrganizationPositions.Requests.Queries;

public sealed record GetOrganizationPositionDetailsRequest(int Id = default) : IQuery<OrganizationPositionDto?>;
