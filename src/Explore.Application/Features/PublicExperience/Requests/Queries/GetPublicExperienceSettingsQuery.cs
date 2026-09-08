using Explore.Application.DTOs.Onboarding;
using MediatR;

namespace Explore.Application.Features.PublicExperience.Requests.Queries;

public sealed record GetPublicExperienceSettingsQuery : IRequest<PublicExperienceSettingsDto>
{
}
