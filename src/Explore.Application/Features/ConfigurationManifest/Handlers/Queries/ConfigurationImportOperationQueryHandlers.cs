namespace Explore.Application.Features.ConfigurationManifest.Handlers.Queries;

using System.Collections.Immutable;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Application.Contracts.Operations;

public sealed class GetInstanceConfigurationImportReceiptQueryHandler(
    ConfigurationImportApplyService service) : IQueryHandler<
        GetInstanceConfigurationImportReceiptQuery,
        ConfigurationImportOperationResult>
{
    public Task<ConfigurationImportOperationResult> QueryAsync(
        GetInstanceConfigurationImportReceiptQuery request,
        CancellationToken cancellationToken) =>
        service.GetReceiptAsync(
            request.OperationId,
            ConfigurationImportTarget.ForInstance(),
            cancellationToken);
}

public sealed class GetTenantConfigurationImportReceiptQueryHandler(
    ConfigurationImportApplyService service) : IQueryHandler<
        GetTenantConfigurationImportReceiptQuery,
        ConfigurationImportOperationResult>
{
    public Task<ConfigurationImportOperationResult> QueryAsync(
        GetTenantConfigurationImportReceiptQuery request,
        CancellationToken cancellationToken) =>
        service.GetReceiptAsync(
            request.OperationId,
            ConfigurationImportTarget.ForTenant(request.TenantId),
            cancellationToken);
}

public sealed class ListInstanceConfigurationImportHistoryQueryHandler(
    ConfigurationImportApplyService service) : IQueryHandler<
        ListInstanceConfigurationImportHistoryQuery,
        ImmutableArray<ConfigurationImportOperationResult>>
{
    public Task<ImmutableArray<ConfigurationImportOperationResult>> QueryAsync(
        ListInstanceConfigurationImportHistoryQuery request,
        CancellationToken cancellationToken) =>
        service.ListAsync(
            ConfigurationImportTarget.ForInstance(),
            request.MaximumCount,
            cancellationToken);
}

public sealed class ListTenantConfigurationImportHistoryQueryHandler(
    ConfigurationImportApplyService service) : IQueryHandler<
        ListTenantConfigurationImportHistoryQuery,
        ImmutableArray<ConfigurationImportOperationResult>>
{
    public Task<ImmutableArray<ConfigurationImportOperationResult>> QueryAsync(
        ListTenantConfigurationImportHistoryQuery request,
        CancellationToken cancellationToken) =>
        service.ListAsync(
            ConfigurationImportTarget.ForTenant(request.TenantId),
            request.MaximumCount,
            cancellationToken);
}
