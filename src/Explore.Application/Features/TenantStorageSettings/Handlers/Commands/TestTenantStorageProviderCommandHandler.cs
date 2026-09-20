using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Exceptions;
using Explore.Application.Features.TenantStorageSettings.Requests.Commands;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.TenantStorageSettings.Handlers.Commands;

public sealed class TestTenantStorageProviderCommandHandler(
    ITenantContext tenantContext,
    IAdminContext adminContext,
    ITenantStorageSettingService storageSettingService)
    : ICommandHandler<TestTenantStorageProviderCommand, InstanceStorageProviderStatusDto>
{
    public async Task<InstanceStorageProviderStatusDto> ExecuteAsync(
        TestTenantStorageProviderCommand request,
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
