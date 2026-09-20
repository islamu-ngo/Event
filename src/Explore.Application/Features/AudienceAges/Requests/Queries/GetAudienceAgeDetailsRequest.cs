using Explore.Application.DTOs.AudienceAge;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.AudienceAges.Requests.Queries;

public sealed record GetAudienceAgeDetailsRequest(int Id = default) : IQuery<AudienceAgeDto?>;
