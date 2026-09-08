using Explore.Application.DTOs.AudienceAge;
using MediatR;

namespace Explore.Application.Features.AudienceAges.Requests.Queries;

public sealed record GetAudienceAgeDetailsRequest(int Id = default) : IRequest<AudienceAgeDto>;
