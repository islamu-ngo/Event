namespace Explore.Application.Features.ConfigurationManifest.Handlers.Commands;

using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using MediatR;

public sealed class CreateInstanceConfigurationImportSessionCommandHandler(
    ConfigurationImportSessionApplicationService service)
    : IRequestHandler<
        CreateInstanceConfigurationImportSessionCommand,
        ConfigurationImportSessionCreatedResult>
{
    public Task<ConfigurationImportSessionCreatedResult> Handle(
        CreateInstanceConfigurationImportSessionCommand request,
        CancellationToken cancellationToken) =>
        service.CreateInstanceAsync(request.Artifact, cancellationToken);
}

public sealed class CreateTenantConfigurationImportSessionCommandHandler(
    ConfigurationImportSessionApplicationService service)
    : IRequestHandler<
        CreateTenantConfigurationImportSessionCommand,
        ConfigurationImportSessionCreatedResult>
{
    public Task<ConfigurationImportSessionCreatedResult> Handle(
        CreateTenantConfigurationImportSessionCommand request,
        CancellationToken cancellationToken) =>
        service.CreateTenantAsync(
            request.TenantId,
            request.Artifact,
            cancellationToken);
}

public sealed class PreviewInstanceConfigurationImportSessionCommandHandler(
    ConfigurationImportSessionApplicationService service)
    : IRequestHandler<
        PreviewInstanceConfigurationImportSessionCommand,
        ConfigurationImportPreviewResult>
{
    public Task<ConfigurationImportPreviewResult> Handle(
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
    : IRequestHandler<
        PreviewTenantConfigurationImportSessionCommand,
        ConfigurationImportPreviewResult>
{
    public Task<ConfigurationImportPreviewResult> Handle(
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
    : IRequestHandler<CancelInstanceConfigurationImportSessionCommand>
{
    public async Task Handle(
        CancelInstanceConfigurationImportSessionCommand request,
        CancellationToken cancellationToken) =>
        await service.CancelInstanceAsync(
            request.SessionId,
            request.AccessToken,
            cancellationToken);
}

public sealed class CancelTenantConfigurationImportSessionCommandHandler(
    ConfigurationImportSessionApplicationService service)
    : IRequestHandler<CancelTenantConfigurationImportSessionCommand>
{
    public async Task Handle(
        CancelTenantConfigurationImportSessionCommand request,
        CancellationToken cancellationToken) =>
        await service.CancelTenantAsync(
            request.TenantId,
            request.SessionId,
            request.AccessToken,
            cancellationToken);
}
