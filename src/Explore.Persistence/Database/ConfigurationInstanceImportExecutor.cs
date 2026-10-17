using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;

namespace Explore.Persistence.Database;

internal sealed class ConfigurationInstanceImportExecutor(
    IServiceProvider provider, IServiceScopeFactory scopes,
    IGlobalExploreDbContextFactory globalFactory) : IConfigurationInstanceImportExecutor
{
    public async Task<ConfigurationImportSessionCreatedResult> CreateAsync(
        ReadOnlyMemory<byte> artifact, CancellationToken cancellationToken)
    {
        artifact = artifact.ToArray();
        await using var scope = await CreateOwnerAsync(cancellationToken);
        return await scope.ServiceProvider.GetRequiredService<ConfigurationImportSessionApplicationService>()
            .CreateInstanceAsync(artifact, cancellationToken);
    }

    public async Task CancelAsync(
        Guid sessionId, string accessToken, CancellationToken cancellationToken)
    {
        await using var scope = await CreateOwnerAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<ConfigurationImportSessionApplicationService>()
            .CancelInstanceAsync(sessionId, accessToken, cancellationToken);
    }

    public async Task<ConfigurationManifestExportResult> ReadSnapshotAsync(
        ConfigurationManifestExportView view, CancellationToken cancellationToken)
    {
        await using var scope = await CreateOwnerAsync(cancellationToken);
        var reader = scope.ServiceProvider.GetRequiredService<ConfigurationManifestCurrentStateReader>();
        return await scope.ServiceProvider.GetRequiredService<IUnitOfWork>()
            .ExecuteSerializableAsync(
                token => reader.ReadImportSnapshotAsync(view, null, token), cancellationToken);
    }

    public async Task<ConfigurationImportPreviewResult> PreviewAsync(
        Guid sessionId, string accessToken, ConfigurationImportPreviewRequest request,
        CancellationToken cancellationToken)
    {
        request = request?.Capture()
            ?? throw new ConfigurationImportSessionException(ConfigurationImportFailureCodes.ContractInvalid);
        await using var scope = await CreateOwnerAsync(cancellationToken);
        return await scope.ServiceProvider.GetRequiredService<ConfigurationImportSessionApplicationService>()
            .PreviewInstanceAsync(sessionId, accessToken, request, cancellationToken);
    }

    public async Task<ConfigurationImportOperationResult> ApplyAsync(
        Guid sessionId, string accessToken, ConfigurationImportPreviewRequest request,
        Guid? sourceOperationId, Guid? managedScheduleId, CancellationToken cancellationToken)
    {
        request = request?.Capture()
            ?? throw new ConfigurationImportSessionException(ConfigurationImportFailureCodes.ContractInvalid);
        await using var scope = await CreateOwnerAsync(cancellationToken);
        var subject = scope.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>().HumanScope?.Subject
            ?? throw new UnauthorizedAccessException("Instance import requires its selected authenticated human owner.");
        return await scope.ServiceProvider.GetRequiredService<ConfigurationImportApplyService>()
            .ApplyInstanceAsync(sessionId, accessToken, request, subject,
                sourceOperationId, managedScheduleId, cancellationToken);
    }

    public async Task<ConfigurationImportOperationResult> GetReceiptAsync(
        Guid operationId, CancellationToken cancellationToken)
    {
        await using var owner = await CreateOwnerAsync(cancellationToken);
        return await owner.ServiceProvider.GetRequiredService<ConfigurationImportApplyService>()
            .GetReceiptAsync(operationId, ConfigurationImportTarget.ForInstance(), cancellationToken);
    }

    public async Task<ImmutableArray<ConfigurationImportOperationResult>> ListAsync(
        int maximumCount, CancellationToken cancellationToken)
    {
        await using var owner = await CreateOwnerAsync(cancellationToken);
        return await owner.ServiceProvider.GetRequiredService<ConfigurationImportApplyService>()
            .ListAsync(ConfigurationImportTarget.ForInstance(), maximumCount, cancellationToken);
    }

    public async Task<ConfigurationImportRollbackSessionCreatedResult> CreateRollbackSessionAsync(
        Guid operationId, CancellationToken cancellationToken)
    {
        await using var owner = await CreateOwnerAsync(cancellationToken);
        return await owner.ServiceProvider.GetRequiredService<ConfigurationImportApplyService>()
            .CreateRollbackSessionAsync(
                operationId, ConfigurationImportTarget.ForInstance(), cancellationToken);
    }

    private async ValueTask<AsyncServiceScope> CreateOwnerAsync(CancellationToken cancellationToken)
    {
        var subject = await PolicyMutationExecutionScopeFactory.ResolveSubjectAsync(provider, cancellationToken);
        var lifetime = subject is null ? null
            : await PolicyMutationExecutionScopeFactory.ResolveLifetimeAsync(provider, cancellationToken);
        var scope = scopes.CreateAsyncScope();
        ExploreDbContext? context = null;
        bool captured = false;
        try
        {
            context = await globalFactory.CreateDbContextAsync(cancellationToken);
            context.BindConfigurationInstanceImportOwner();
            context.CurrentUserService = provider.GetService<ICurrentUserService>();
            if (provider.GetService<ITenantContextAccessor>()?.TenantId is { } tenantId)
                scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(tenantId);
            var lease = scope.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>();
            lease.Bind(context, null);
            if (subject is not null)
                lease.BindHuman(subject, HumanAdministratorCommitPurpose.ConfigurationImport,
                    context.Database.IsNpgsql()
                        ? PostgresNativeQueueEnrollment.Bind(provider.GetRequiredService<IConfiguration>()) : null, lifetime);
            _ = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            captured = true;
            return scope;
        }
        catch
        {
            if (!captured && context is not null)
                await context.DisposeAsync();
            await scope.DisposeAsync();
            throw;
        }
    }
}
