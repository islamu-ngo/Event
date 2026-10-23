namespace Explore.Application.Features.ConfigurationManifest.Handlers.Commands;

using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.DependencyInjection;

public sealed class ApplyInstanceConfigurationImportCommandHandler(
    IConfigurationInstanceImportExecutor service) : ICommandHandler<
        ApplyInstanceConfigurationImportCommand,
        ConfigurationImportOperationResult>
{
    public Task<ConfigurationImportOperationResult> ExecuteAsync(
        ApplyInstanceConfigurationImportCommand request,
        CancellationToken cancellationToken) =>
        service.ApplyAsync(
            request.SessionId,
            request.AccessToken,
            request.Preview,
            request.RollbackOfOperationId,
            request.ManagedScheduleId,
            cancellationToken);
}

public sealed class ApplyTenantConfigurationImportCommandHandler(
    IPolicyMutationExecutionScopeFactory scopes) : ICommandHandler<
        ApplyTenantConfigurationImportCommand,
        ConfigurationImportOperationResult>
{
    public async Task<ConfigurationImportOperationResult> ExecuteAsync(
        ApplyTenantConfigurationImportCommand request,
        CancellationToken cancellationToken)
    {
        var preview = request.Preview?.Capture()
            ?? throw new ConfigurationImportSessionException(ConfigurationImportFailureCodes.ContractInvalid);
        await using var selected = await scopes.CreateAsync(new PolicyMutationScope(request.TenantId), cancellationToken);
        var subject = selected.Services.GetRequiredService<IHumanCommitScopePreparation>().Subject
            ?? throw new UnauthorizedAccessException("Tenant import requires its selected authenticated human owner.");
        return await selected.Services.GetRequiredService<ConfigurationImportApplyService>().ApplyTenantAsync(
            request.TenantId,
            request.SessionId,
            request.AccessToken,
            preview,
            subject,
            request.RollbackOfOperationId,
            request.ManagedScheduleId,
            cancellationToken);
    }
}

public sealed class CreateInstanceConfigurationRollbackSessionCommandHandler(
    IConfigurationInstanceImportExecutor service) : ICommandHandler<
        CreateInstanceConfigurationRollbackSessionCommand,
        ConfigurationImportRollbackSessionCreatedResult>
{
    public Task<ConfigurationImportRollbackSessionCreatedResult> ExecuteAsync(
        CreateInstanceConfigurationRollbackSessionCommand request,
        CancellationToken cancellationToken) =>
        service.CreateRollbackSessionAsync(
            request.OperationId,
            cancellationToken);
}

public sealed class CreateTenantConfigurationRollbackSessionCommandHandler(
    IPolicyMutationExecutionScopeFactory scopes) : ICommandHandler<
        CreateTenantConfigurationRollbackSessionCommand,
        ConfigurationImportRollbackSessionCreatedResult>
{
    public async Task<ConfigurationImportRollbackSessionCreatedResult> ExecuteAsync(
        CreateTenantConfigurationRollbackSessionCommand request,
        CancellationToken cancellationToken)
    {
        await using var selected = await scopes.CreateAsync(new PolicyMutationScope(request.TenantId), cancellationToken);
        return await selected.Services.GetRequiredService<ConfigurationImportApplyService>().CreateRollbackSessionAsync(
            request.OperationId,
            ConfigurationImportTarget.ForTenant(request.TenantId),
            cancellationToken);
    }
}
