using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.PublicExperience;

namespace Explore.Application.Features.PublicExperience.Requests.Queries;

public sealed record GetPublicExperienceShellQuery : IQuery<PublicExperienceShellDto>
{
}
