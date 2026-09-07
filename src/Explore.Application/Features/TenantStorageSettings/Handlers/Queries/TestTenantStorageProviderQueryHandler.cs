using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Exceptions;
using Explore.Application.Features.TenantStorageSettings.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.TenantStorageSettings.Handlers.Queries;

public sealed class TestTenantStorageProviderQueryHandler(
    ITenantContext tenantContext,
    IAdminContext adminContext,
    ITenantStorageSettingService storageSettingService)
    : IRequestHandler<TestTenantStorageProviderQuery, InstanceStorageProviderStatusDto>
{
    public async Task<InstanceStorageProviderStatusDto> Handle(
        TestTenantStorageProviderQuery request,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId;
        if (!await adminContext.IsTenantAdminAsync(tenantId, cancellationToken)
            && !await adminContext.IsInstanceAdminAsync(cancellationToken))
        {
            throw new AuthorizationException("Only tenant administrators or instance administrators can test tenant storage settings.");
        }

        return await storageSettingService.TestProviderAsync(tenantId, cancellationToken);
    }
}
