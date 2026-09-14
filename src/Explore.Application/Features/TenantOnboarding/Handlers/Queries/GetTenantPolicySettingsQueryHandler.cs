using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.TenantOnboarding.Requests.Queries;

namespace Explore.Application.Features.TenantOnboarding.Handlers.Queries;

public class GetTenantPolicySettingsQueryHandler : IQueryHandler<GetTenantPolicySettingsQuery, TenantPolicySettingsDto>
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

    public async Task<TenantPolicySettingsDto> QueryAsync(GetTenantPolicySettingsQuery request, CancellationToken cancellationToken = default)
    {
        return await _policySettingService.ReadEffectiveTenantSettingsAsync(_tenantContext.TenantId);
    }
}
