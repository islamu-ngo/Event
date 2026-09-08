namespace Explore.Application.Features.ConfigurationManifest.Handlers.Queries;

using System.Collections.Immutable;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using MediatR;

public sealed class GetInstanceConfigurationImportReceiptQueryHandler(
    ConfigurationImportApplyService service) : IRequestHandler<
        GetInstanceConfigurationImportReceiptQuery,
        ConfigurationImportOperationResult>
{
    public Task<ConfigurationImportOperationResult> Handle(
        GetInstanceConfigurationImportReceiptQuery request,
        CancellationToken cancellationToken) =>
        service.GetReceiptAsync(
            request.OperationId,
            ConfigurationImportTarget.ForInstance(),
            cancellationToken);
}

public sealed class GetTenantConfigurationImportReceiptQueryHandler(
    ConfigurationImportApplyService service) : IRequestHandler<
        GetTenantConfigurationImportReceiptQuery,
        ConfigurationImportOperationResult>
{
    public Task<ConfigurationImportOperationResult> Handle(
        GetTenantConfigurationImportReceiptQuery request,
        CancellationToken cancellationToken) =>
        service.GetReceiptAsync(
            request.OperationId,
            ConfigurationImportTarget.ForTenant(request.TenantId),
            cancellationToken);
}

public sealed class ListInstanceConfigurationImportHistoryQueryHandler(
    ConfigurationImportApplyService service) : IRequestHandler<
        ListInstanceConfigurationImportHistoryQuery,
        ImmutableArray<ConfigurationImportOperationResult>>
{
    public Task<ImmutableArray<ConfigurationImportOperationResult>> Handle(
        ListInstanceConfigurationImportHistoryQuery request,
        CancellationToken cancellationToken) =>
        service.ListAsync(
            ConfigurationImportTarget.ForInstance(),
            request.MaximumCount,
            cancellationToken);
}

public sealed class ListTenantConfigurationImportHistoryQueryHandler(
    ConfigurationImportApplyService service) : IRequestHandler<
        ListTenantConfigurationImportHistoryQuery,
        ImmutableArray<ConfigurationImportOperationResult>>
{
    public Task<ImmutableArray<ConfigurationImportOperationResult>> Handle(
        ListTenantConfigurationImportHistoryQuery request,
        CancellationToken cancellationToken) =>
        service.ListAsync(
            ConfigurationImportTarget.ForTenant(request.TenantId),
            request.MaximumCount,
            cancellationToken);
}
