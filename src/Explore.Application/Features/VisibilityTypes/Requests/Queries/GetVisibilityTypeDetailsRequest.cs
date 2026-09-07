using Explore.Application.DTOs.VisibilityType;
using MediatR;

namespace Explore.Application.Features.VisibilityTypes.Requests.Queries;

public sealed record GetVisibilityTypeDetailsRequest(int Id = default) : IRequest<VisibilityTypeDto>;
