using Explore.Application.DTOs.AudienceGender;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.AudienceGenders.Requests.Queries;

public sealed record GetAudienceGenderDetailsRequest(int Id = default) : IQuery<AudienceGenderDto?>;
