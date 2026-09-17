using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Analytics;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Queries;

public sealed record GetAnalyticsGovernanceSettingsQuery : IQuery<AnalyticsGovernanceSettingsDto>;
