using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Queries;

public sealed record TestInstanceStorageProviderQuery : IQuery<InstanceStorageProviderStatusDto>
{
    public required Guid UserId { get; init; }
}
