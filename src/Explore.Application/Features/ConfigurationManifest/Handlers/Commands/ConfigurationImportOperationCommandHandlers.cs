namespace Explore.Application.Features.ConfigurationManifest.Handlers.Commands;

using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using MediatR;

public sealed class ApplyInstanceConfigurationImportCommandHandler(
    ConfigurationImportApplyService service) : IRequestHandler<
        ApplyInstanceConfigurationImportCommand,
        ConfigurationImportOperationResult>
{
    public Task<ConfigurationImportOperationResult> Handle(
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
    ConfigurationImportApplyService service) : IRequestHandler<
        ApplyTenantConfigurationImportCommand,
        ConfigurationImportOperationResult>
{
    public Task<ConfigurationImportOperationResult> Handle(
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
    ConfigurationImportApplyService service) : IRequestHandler<
        CreateInstanceConfigurationRollbackSessionCommand,
        ConfigurationImportRollbackSessionCreatedResult>
{
    public Task<ConfigurationImportRollbackSessionCreatedResult> Handle(
        CreateInstanceConfigurationRollbackSessionCommand request,
        CancellationToken cancellationToken) =>
        service.CreateRollbackSessionAsync(
            request.OperationId,
            ConfigurationImportTarget.ForInstance(),
            cancellationToken);
}

public sealed class CreateTenantConfigurationRollbackSessionCommandHandler(
    ConfigurationImportApplyService service) : IRequestHandler<
        CreateTenantConfigurationRollbackSessionCommand,
        ConfigurationImportRollbackSessionCreatedResult>
{
    public Task<ConfigurationImportRollbackSessionCreatedResult> Handle(
        CreateTenantConfigurationRollbackSessionCommand request,
        CancellationToken cancellationToken) =>
        service.CreateRollbackSessionAsync(
            request.OperationId,
            ConfigurationImportTarget.ForTenant(request.TenantId),
            cancellationToken);
}
