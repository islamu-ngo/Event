using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Queries;

/// <summary>The existing authenticated-request plus accepted-secret branch of the storage probe.</summary>
public sealed record TestInstanceStorageProviderDuringSetupQuery : IQuery<InstanceStorageProviderStatusDto>;
