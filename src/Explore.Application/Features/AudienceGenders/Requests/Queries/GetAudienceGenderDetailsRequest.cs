using Explore.Application.DTOs.AudienceGender;
using MediatR;

namespace Explore.Application.Features.AudienceGenders.Requests.Queries;

public sealed record GetAudienceGenderDetailsRequest(int Id = default) : IRequest<AudienceGenderDto>;
