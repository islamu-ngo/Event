using Explore.Application.DTOs.Onboarding;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Queries;

public sealed record GetResolverConfigurationQuery : IRequest<ResolverConfigurationDto>
{
}
