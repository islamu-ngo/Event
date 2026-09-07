using Explore.Application.DTOs.Analytics;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Queries;

public sealed record GetAnalyticsGovernanceSettingsQuery : IRequest<AnalyticsGovernanceSettingsDto>;
