namespace Explore.Application.Features.ConfigurationManifest.Handlers.Queries;

using System.Collections.Immutable;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.DependencyInjection;

public sealed class GetInstanceConfigurationImportReceiptQueryHandler(
    IConfigurationInstanceImportExecutor service) : IQueryHandler<
        GetInstanceConfigurationImportReceiptQuery,
        ConfigurationImportOperationResult>
{
    public Task<ConfigurationImportOperationResult> QueryAsync(
        GetInstanceConfigurationImportReceiptQuery request,
        CancellationToken cancellationToken) =>
        service.GetReceiptAsync(
            request.OperationId,
            cancellationToken);
}

public sealed class GetTenantConfigurationImportReceiptQueryHandler(
    IPolicyMutationExecutionScopeFactory scopes) : IQueryHandler<
        GetTenantConfigurationImportReceiptQuery,
        ConfigurationImportOperationResult>
{
    public async Task<ConfigurationImportOperationResult> QueryAsync(
        GetTenantConfigurationImportReceiptQuery request,
        CancellationToken cancellationToken)
    {
        await using var selected = await scopes.CreateAsync(new PolicyMutationScope(request.TenantId), cancellationToken);
        return await selected.Services.GetRequiredService<ConfigurationImportApplyService>().GetReceiptAsync(
            request.OperationId,
            ConfigurationImportTarget.ForTenant(request.TenantId),
            cancellationToken);
    }
}

public sealed class ListInstanceConfigurationImportHistoryQueryHandler(
    IConfigurationInstanceImportExecutor service) : IQueryHandler<
        ListInstanceConfigurationImportHistoryQuery,
        ImmutableArray<ConfigurationImportOperationResult>>
{
    public Task<ImmutableArray<ConfigurationImportOperationResult>> QueryAsync(
        ListInstanceConfigurationImportHistoryQuery request,
        CancellationToken cancellationToken) =>
        service.ListAsync(
            request.MaximumCount,
            cancellationToken);
}

public sealed class ListTenantConfigurationImportHistoryQueryHandler(
    IPolicyMutationExecutionScopeFactory scopes) : IQueryHandler<
        ListTenantConfigurationImportHistoryQuery,
        ImmutableArray<ConfigurationImportOperationResult>>
{
    public async Task<ImmutableArray<ConfigurationImportOperationResult>> QueryAsync(
        ListTenantConfigurationImportHistoryQuery request,
        CancellationToken cancellationToken)
    {
        await using var selected = await scopes.CreateAsync(new PolicyMutationScope(request.TenantId), cancellationToken);
        return await selected.Services.GetRequiredService<ConfigurationImportApplyService>().ListAsync(
            ConfigurationImportTarget.ForTenant(request.TenantId),
            request.MaximumCount,
            cancellationToken);
    }
}
