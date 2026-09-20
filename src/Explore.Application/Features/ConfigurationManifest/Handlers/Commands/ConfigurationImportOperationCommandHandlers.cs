namespace Explore.Application.Features.ConfigurationManifest.Handlers.Commands;

using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Contracts.Operations;

public sealed class ApplyInstanceConfigurationImportCommandHandler(
    ConfigurationImportApplyService service) : ICommandHandler<
        ApplyInstanceConfigurationImportCommand,
        ConfigurationImportOperationResult>
{
    public Task<ConfigurationImportOperationResult> ExecuteAsync(
        ApplyInstanceConfigurationImportCommand request,
        CancellationToken cancellationToken) =>
        service.ApplyInstanceAsync(
            request.SessionId,
            request.AccessToken,
            request.Preview,
            request.RollbackOfOperationId,
            request.ManagedScheduleId,
            cancellationToken);
}

public sealed class ApplyTenantConfigurationImportCommandHandler(
    ConfigurationImportApplyService service) : ICommandHandler<
        ApplyTenantConfigurationImportCommand,
        ConfigurationImportOperationResult>
{
    public Task<ConfigurationImportOperationResult> ExecuteAsync(
        ApplyTenantConfigurationImportCommand request,
        CancellationToken cancellationToken) =>
        service.ApplyTenantAsync(
            request.TenantId,
            request.SessionId,
            request.AccessToken,
            request.Preview,
            request.RollbackOfOperationId,
            request.ManagedScheduleId,
            cancellationToken);
}

public sealed class CreateInstanceConfigurationRollbackSessionCommandHandler(
    ConfigurationImportApplyService service) : ICommandHandler<
        CreateInstanceConfigurationRollbackSessionCommand,
        ConfigurationImportRollbackSessionCreatedResult>
{
    public Task<ConfigurationImportRollbackSessionCreatedResult> ExecuteAsync(
        CreateInstanceConfigurationRollbackSessionCommand request,
        CancellationToken cancellationToken) =>
        service.CreateRollbackSessionAsync(
            request.OperationId,
            ConfigurationImportTarget.ForInstance(),
            cancellationToken);
}

public sealed class CreateTenantConfigurationRollbackSessionCommandHandler(
    ConfigurationImportApplyService service) : ICommandHandler<
        CreateTenantConfigurationRollbackSessionCommand,
        ConfigurationImportRollbackSessionCreatedResult>
{
    public Task<ConfigurationImportRollbackSessionCreatedResult> ExecuteAsync(
        CreateTenantConfigurationRollbackSessionCommand request,
        CancellationToken cancellationToken) =>
        service.CreateRollbackSessionAsync(
            request.OperationId,
            ConfigurationImportTarget.ForTenant(request.TenantId),
            cancellationToken);
}
