using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Features.TenantOnboarding.Requests.Queries;

public sealed record GetTenantPolicySettingsQuery : IQuery<TenantPolicySettingsDto>
{
}
