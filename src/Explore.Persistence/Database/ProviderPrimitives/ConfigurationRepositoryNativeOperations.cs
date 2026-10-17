using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Persistence.Entities;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Explore.Persistence.Database.ProviderPrimitives;

/// <summary>Fixed native import operations on the caller's existing context and transaction.</summary>
internal sealed class ConfigurationRepositoryNativeOperations
{
    private readonly ExploreDbContext context;

    private ConfigurationRepositoryNativeOperations(ExploreDbContext context) => this.context = context;

    internal static ConfigurationRepositoryNativeOperations? ForInstanceImport(ExploreDbContext context) =>
        context.IsConfigurationInstanceImportOwner && context.Database.IsNpgsql() ? new(context) : null;

    internal static ConfigurationRepositoryNativeOperations? ForManifestReplay(ExploreDbContext context) =>
        context.Database.IsNpgsql() ? new(context) : null;

    internal Task<ConfigurationImportStoredArtifact?> ReadArtifactAsync(Guid id, CancellationToken token) =>
        PostgresConfigurationImportStateContract.ReadArtifactAsync(context, id, token);

    internal Task StoreArtifactAsync(ConfigurationImportStoredArtifact artifact, CancellationToken token) =>
        PostgresConfigurationImportStateContract.StoreArtifactAsync(context, artifact, token);

    internal Task DeleteArtifactAsync(Guid id, CancellationToken token) =>
        PostgresConfigurationImportStateContract.DeleteArtifactAsync(context, id, token);

    internal Task CreateSessionAsync(ConfigurationImportSession session, CancellationToken token) =>
        PostgresConfigurationImportStateContract.CreateSessionAsync(context, session, token);

    internal Task<ConfigurationImportSession?> ReadSessionAsync(Guid id, CancellationToken token) =>
        PostgresConfigurationImportStateContract.ReadSessionAsync(context, id, token);

    internal Task WriteSessionAsync(ConfigurationImportSession session, long revision, CancellationToken token) =>
        PostgresConfigurationImportStateContract.WriteSessionAsync(context, session, revision, token);

    internal Task StoreOperationAsync(ConfigurationImportOperation operation, CancellationToken token) =>
        PostgresConfigurationImportReceiptContract.StoreOperationAsync(context, operation, token);

    internal Task<ConfigurationImportOperation?> ReadOperationAsync(Guid id, string authority, CancellationToken token) =>
        PostgresConfigurationImportReceiptContract.ReadOperationAsync(context, id, authority, token);

    internal Task<IReadOnlyList<ConfigurationImportOperation>> ListOperationsAsync(
        string authority, int maximumCount, CancellationToken token) =>
        PostgresConfigurationImportReceiptContract.ListOperationsAsync(context, authority, maximumCount, token);

    internal Task<ConfigurationManifestOperation?> ReadLatestAppliedBootstrapAsync(CancellationToken token) =>
        PostgresConfigurationImportSnapshotContract.ReadLatestAppliedBootstrapAsync(context, token);

    internal async Task<IReadOnlyList<ConfigurationManifestTenantResult>> ReadManifestResultsAsync(
        Guid operationId, CancellationToken token) =>
        await context.ConfigurationManifestTenantResults
            .FromSqlRaw($"SELECT * FROM {PostgresManifestReplayContract.FunctionSql(context)}(@operation)",
                new NpgsqlParameter("operation", operationId))
            .IgnoreTenantFilter(TenantFilterBypassReasons.ConfigurationManifestOperationReplay)
            .AsNoTracking()
            .ToListAsync(token);

    internal Task<PublicationPolicyMutationSnapshot> ReadTenantSnapshotAsync(Guid tenant, CancellationToken token) =>
        PostgresConfigurationImportPublicationPolicyContract.ReadTenantSnapshotAsync(context, tenant, token);

    internal Task<PublicationPolicyMutationSnapshot> ReadInstanceSnapshotAsync(CancellationToken token) =>
        PostgresConfigurationImportPublicationPolicyContract.ReadInstanceSnapshotAsync(context, token);

    internal Task<CoordinatedSettingMutationWriteResult> WriteTenantAsync(Guid tenant,
        ImmutableArray<PublicationPolicySettingMutation> mutations, Guid? actor, DateTime occurred, CancellationToken token) =>
        PostgresConfigurationImportPublicationPolicyContract.WriteTenantAsync(context, tenant, mutations, actor, occurred, token);

    internal Task<CoordinatedSettingMutationWriteResult> WriteInstanceAsync(
        ImmutableArray<PublicationPolicySettingMutation> mutations, Guid actor, DateTime occurred, CancellationToken token) =>
        PostgresConfigurationImportPublicationPolicyContract.WriteInstanceAsync(context, mutations, actor, occurred, token);
}
