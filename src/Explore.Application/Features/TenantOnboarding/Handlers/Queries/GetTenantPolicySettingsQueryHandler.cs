using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.TenantOnboarding.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.TenantOnboarding.Handlers.Queries;

public class GetTenantPolicySettingsQueryHandler : IRequestHandler<GetTenantPolicySettingsQuery, TenantPolicySettingsDto>
{
    private readonly ITenantContext _tenantContext;
    private readonly ITenantPolicySettingService _policySettingService;

    public GetTenantPolicySettingsQueryHandler(
        ITenantContext tenantContext,
        ITenantPolicySettingService policySettingService)
    {
        _tenantContext = tenantContext;
        _policySettingService = policySettingService;
    }

    public async Task<TenantPolicySettingsDto> Handle(GetTenantPolicySettingsQuery request, CancellationToken cancellationToken)
    {
        return await _policySettingService.ReadEffectiveTenantSettingsAsync(_tenantContext.TenantId);
    }
}
