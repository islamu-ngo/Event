using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Commands;

/// <summary>Only the existing accounting setup entrypoint may retain this source kind.</summary>
public sealed record RecalculateInstanceStorageUsageDuringSetupCommand : ICommand<InstanceStorageUsageDto>;
