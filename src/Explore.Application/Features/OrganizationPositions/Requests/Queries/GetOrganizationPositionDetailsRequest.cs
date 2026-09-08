using Explore.Application.DTOs.OrganizationPosition;
using MediatR;

namespace Explore.Application.Features.OrganizationPositions.Requests.Queries;

public sealed record GetOrganizationPositionDetailsRequest(int Id = default) : IRequest<OrganizationPositionDto>;
