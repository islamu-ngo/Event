using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Http;
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
        ConfigurationManifestExportView view, ConfigurationManifestExportScope snapshotScope,
        CancellationToken cancellationToken)
    {
        var requests = provider.GetRequiredService<IHttpContextAccessor>();
        var clock = provider.GetRequiredService<TimeProvider>();
        var request = requests.HttpContext
            ?? throw new UnauthorizedAccessException("Instance snapshots require a human request.");
        var principal = request.User;
        var identities = principal.Identities.ToArray();
        var account = principal.GetProviderIdentity()?.AccountKey
            ?? throw new UnauthorizedAccessException("Instance snapshots require an authenticated provider account.");
        HumanAuthenticationLifetime lifetime;
        ConfigurationManifestExportResult result;
        await using (var scope = await CreateOwnerAsync(cancellationToken, RequireOriginalRequest))
        {
            var reader = scope.ServiceProvider.GetRequiredService<ConfigurationManifestCurrentStateReader>();
            var lease = scope.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>();
            lifetime = lease.HumanLifetime
                ?? throw new UnauthorizedAccessException("Instance snapshots require the original authenticated credential lifetime.");
            RequireLiveCredential();
            result = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>()
                .ExecuteSerializableAsync(
                    token => reader.ReadImportSnapshotAsync(view, snapshotScope, token), cancellationToken);
            lease.RequireOriginalCredential(requests.HttpContext, lifetime);
            RequireLiveCredential();
        }
        RequireLiveCredential();
        return result;

        void RequireOriginalRequest()
        {
            if (!ReferenceEquals(requests.HttpContext, request) || !ReferenceEquals(request.User, principal)
                || !principal.Identities.SequenceEqual(identities, ReferenceEqualityComparer.Instance)
                || principal.GetProviderIdentity()?.AccountKey != account)
                throw new UnauthorizedAccessException("The original snapshot credential changed.");
        }

        void RequireLiveCredential()
        {
            RequireOriginalRequest();
            if (lifetime.AccountKey != account || !lifetime.IsValidAt(clock.GetUtcNow()))
                throw new UnauthorizedAccessException("Instance snapshots require the original live credential.");
        }
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

    private async ValueTask<AsyncServiceScope> CreateOwnerAsync(
        CancellationToken cancellationToken, Action? requireOriginalRequest = null)
    {
        var subject = await PolicyMutationExecutionScopeFactory.ResolveSubjectAsync(provider, cancellationToken);
        requireOriginalRequest?.Invoke();
        var lifetime = subject is null ? null
            : await PolicyMutationExecutionScopeFactory.ResolveLifetimeAsync(provider, cancellationToken);
        requireOriginalRequest?.Invoke();
        var scope = scopes.CreateAsyncScope();
        ExploreDbContext? context = null;
        bool captured = false;
        try
        {
            context = await globalFactory.CreateDbContextAsync(cancellationToken);
            requireOriginalRequest?.Invoke();
            context.BindConfigurationInstanceImportOwner();
            context.CurrentUserService = provider.GetService<ICurrentUserService>();
            if (provider.GetService<ITenantContextAccessor>()?.TenantId is { } tenantId)
                scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(tenantId);
            var lease = scope.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>();
            lease.Bind(context, null);
            if (subject is not null)
            {
                lease.BindHuman(subject, HumanAdministratorCommitPurpose.ConfigurationImport,
                    context.Database.IsNpgsql()
                        ? PostgresNativeQueueEnrollment.Bind(provider.GetRequiredService<IConfiguration>()) : null, lifetime);
                if (lifetime is not null)
                    lease.BindOriginalCredential(provider.GetRequiredService<IHttpContextAccessor>().HttpContext
                        ?? throw new UnauthorizedAccessException("The original authenticated request is missing."));
            }
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
