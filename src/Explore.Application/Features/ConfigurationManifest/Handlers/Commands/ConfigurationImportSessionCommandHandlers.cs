namespace Explore.Application.Features.ConfigurationManifest.Handlers.Commands;

using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.DependencyInjection;

public sealed class CreateInstanceConfigurationImportSessionCommandHandler(
    IConfigurationInstanceImportExecutor service)
    : ICommandHandler<
        CreateInstanceConfigurationImportSessionCommand,
        ConfigurationImportSessionCreatedResult>
{
    public Task<ConfigurationImportSessionCreatedResult> ExecuteAsync(
        CreateInstanceConfigurationImportSessionCommand request,
        CancellationToken cancellationToken) =>
        service.CreateAsync(request.Artifact, cancellationToken);
}

public sealed class CreateTenantConfigurationImportSessionCommandHandler(
    IPolicyMutationExecutionScopeFactory scopes)
    : ICommandHandler<
        CreateTenantConfigurationImportSessionCommand,
        ConfigurationImportSessionCreatedResult>
{
    public async Task<ConfigurationImportSessionCreatedResult> ExecuteAsync(
        CreateTenantConfigurationImportSessionCommand request,
        CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte> artifact = request.Artifact.ToArray();
        await using var selected = await scopes.CreateAsync(new PolicyMutationScope(request.TenantId), cancellationToken);
        return await selected.Services.GetRequiredService<ConfigurationImportSessionApplicationService>().CreateTenantAsync(
            request.TenantId,
            artifact,
            cancellationToken);
    }
}

public sealed class PreviewInstanceConfigurationImportSessionCommandHandler(
    IConfigurationInstanceImportExecutor service)
    : ICommandHandler<
        PreviewInstanceConfigurationImportSessionCommand,
        ConfigurationImportPreviewResult>
{
    public Task<ConfigurationImportPreviewResult> ExecuteAsync(
        PreviewInstanceConfigurationImportSessionCommand request,
        CancellationToken cancellationToken) =>
        service.PreviewAsync(
            request.SessionId,
            request.AccessToken,
            request.Preview,
            cancellationToken);
}

public sealed class PreviewTenantConfigurationImportSessionCommandHandler(
    IPolicyMutationExecutionScopeFactory scopes)
    : ICommandHandler<
        PreviewTenantConfigurationImportSessionCommand,
        ConfigurationImportPreviewResult>
{
    public async Task<ConfigurationImportPreviewResult> ExecuteAsync(
        PreviewTenantConfigurationImportSessionCommand request,
        CancellationToken cancellationToken)
    {
        var preview = request.Preview?.Capture()
            ?? throw new ConfigurationImportSessionException(ConfigurationImportFailureCodes.ContractInvalid);
        await using var selected = await scopes.CreateAsync(new PolicyMutationScope(request.TenantId), cancellationToken);
        return await selected.Services.GetRequiredService<ConfigurationImportSessionApplicationService>().PreviewTenantAsync(
            request.TenantId,
            request.SessionId,
            request.AccessToken,
            preview,
            cancellationToken);
    }
}

public sealed class CancelInstanceConfigurationImportSessionCommandHandler(
    IConfigurationInstanceImportExecutor service)
    : ICommandHandler<CancelInstanceConfigurationImportSessionCommand>
{
    public async Task ExecuteAsync(
        CancelInstanceConfigurationImportSessionCommand request,
        CancellationToken cancellationToken) =>
        await service.CancelAsync(
            request.SessionId,
            request.AccessToken,
            cancellationToken);
}

public sealed class CancelTenantConfigurationImportSessionCommandHandler(
    IPolicyMutationExecutionScopeFactory scopes)
    : ICommandHandler<CancelTenantConfigurationImportSessionCommand>
{
    public async Task ExecuteAsync(
        CancelTenantConfigurationImportSessionCommand request,
        CancellationToken cancellationToken)
    {
        await using var selected = await scopes.CreateAsync(new PolicyMutationScope(request.TenantId), cancellationToken);
        await selected.Services.GetRequiredService<ConfigurationImportSessionApplicationService>().CancelTenantAsync(
            request.TenantId,
            request.SessionId,
            request.AccessToken,
            cancellationToken);
    }
}
