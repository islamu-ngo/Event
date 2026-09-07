using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Exceptions;
using Explore.Application.Features.TenantStorageSettings.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.TenantStorageSettings.Handlers.Queries;

public sealed class GetTenantStorageSettingsQueryHandler
    : IRequestHandler<GetTenantStorageSettingsQuery, TenantStorageSettingsDto>
{
    private readonly ITenantContext _tenantContext;
    private readonly IAdminContext _adminContext;
    private readonly ITenantStorageSettingService _storageSettingService;

    public GetTenantStorageSettingsQueryHandler(
        ITenantContext tenantContext,
        IAdminContext adminContext,
        ITenantStorageSettingService storageSettingService)
    {
        _tenantContext = tenantContext;
        _adminContext = adminContext;
        _storageSettingService = storageSettingService;
    }

    public async Task<TenantStorageSettingsDto> Handle(
        GetTenantStorageSettingsQuery request,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        if (!await IsAuthorizedAsync(tenantId, cancellationToken))
        {
            throw new AuthorizationException("Only tenant administrators or instance administrators can read tenant storage settings.");
        }

        return await _storageSettingService.ReadSettingsAsync(tenantId, cancellationToken);
    }

    private async Task<bool> IsAuthorizedAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (await _adminContext.IsTenantAdminAsync(tenantId, cancellationToken))
        {
            return true;
        }

        return await _adminContext.IsInstanceAdminAsync(cancellationToken);
    }
}
