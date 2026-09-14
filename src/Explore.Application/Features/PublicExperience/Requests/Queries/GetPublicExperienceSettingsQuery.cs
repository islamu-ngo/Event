using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Features.PublicExperience.Requests.Queries;

public sealed record GetPublicExperienceSettingsQuery : IQuery<PublicExperienceSettingsDto>
{
}
