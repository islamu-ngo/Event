using Explore.Application.DTOs.AudienceAge;
using MediatR;

namespace Explore.Application.Features.AudienceAges.Requests.Queries;

public sealed record GetAudienceAgeListRequest : IRequest<List<AudienceAgeListDto>>
{
}
