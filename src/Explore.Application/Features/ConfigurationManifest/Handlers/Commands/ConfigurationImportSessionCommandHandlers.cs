namespace Explore.Application.Features.ConfigurationManifest.Handlers.Commands;

using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Contracts.Operations;

public sealed class CreateInstanceConfigurationImportSessionCommandHandler(
    ConfigurationImportSessionApplicationService service)
    : ICommandHandler<
        CreateInstanceConfigurationImportSessionCommand,
        ConfigurationImportSessionCreatedResult>
{
    public Task<ConfigurationImportSessionCreatedResult> ExecuteAsync(
        CreateInstanceConfigurationImportSessionCommand request,
        CancellationToken cancellationToken) =>
        service.CreateInstanceAsync(request.Artifact, cancellationToken);
}

public sealed class CreateTenantConfigurationImportSessionCommandHandler(
    ConfigurationImportSessionApplicationService service)
    : ICommandHandler<
        CreateTenantConfigurationImportSessionCommand,
        ConfigurationImportSessionCreatedResult>
{
    public Task<ConfigurationImportSessionCreatedResult> ExecuteAsync(
        CreateTenantConfigurationImportSessionCommand request,
        CancellationToken cancellationToken) =>
        service.CreateTenantAsync(
            request.TenantId,
            request.Artifact,
            cancellationToken);
}

public sealed class PreviewInstanceConfigurationImportSessionCommandHandler(
    ConfigurationImportSessionApplicationService service)
    : ICommandHandler<
        PreviewInstanceConfigurationImportSessionCommand,
        ConfigurationImportPreviewResult>
{
    public Task<ConfigurationImportPreviewResult> ExecuteAsync(
        PreviewInstanceConfigurationImportSessionCommand request,
        CancellationToken cancellationToken) =>
        service.PreviewInstanceAsync(
            request.SessionId,
            request.AccessToken,
            request.Preview,
            cancellationToken);
}

public sealed class PreviewTenantConfigurationImportSessionCommandHandler(
    ConfigurationImportSessionApplicationService service)
    : ICommandHandler<
        PreviewTenantConfigurationImportSessionCommand,
        ConfigurationImportPreviewResult>
{
    public Task<ConfigurationImportPreviewResult> ExecuteAsync(
        PreviewTenantConfigurationImportSessionCommand request,
        CancellationToken cancellationToken) =>
        service.PreviewTenantAsync(
            request.TenantId,
            request.SessionId,
            request.AccessToken,
            request.Preview,
            cancellationToken);
}

public sealed class CancelInstanceConfigurationImportSessionCommandHandler(
    ConfigurationImportSessionApplicationService service)
    : ICommandHandler<CancelInstanceConfigurationImportSessionCommand>
{
    public async Task ExecuteAsync(
        CancelInstanceConfigurationImportSessionCommand request,
        CancellationToken cancellationToken) =>
        await service.CancelInstanceAsync(
            request.SessionId,
            request.AccessToken,
            cancellationToken);
}

public sealed class CancelTenantConfigurationImportSessionCommandHandler(
    ConfigurationImportSessionApplicationService service)
    : ICommandHandler<CancelTenantConfigurationImportSessionCommand>
{
    public async Task ExecuteAsync(
        CancelTenantConfigurationImportSessionCommand request,
        CancellationToken cancellationToken) =>
        await service.CancelTenantAsync(
            request.TenantId,
            request.SessionId,
            request.AccessToken,
            cancellationToken);
}
