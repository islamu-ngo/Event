using Explore.Application.DTOs.Onboarding;
using MediatR;

namespace Explore.Application.Features.TenantOnboarding.Requests.Queries;

public sealed record GetTenantPolicySettingsQuery : IRequest<TenantPolicySettingsDto>
{
}
