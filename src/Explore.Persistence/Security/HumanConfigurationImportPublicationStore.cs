using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Security;

/// <summary>
/// Original publication rows, explicit absence and canonical metadata on the selected
/// human import connector. Only this writer can record its private completion.
/// </summary>
internal sealed class HumanConfigurationImportPublicationStore : IHumanConfigurationImportPublicationStore
{
    private readonly ExploreDbContext context;
    private readonly ExploreDbContextExecutionLease lease;
    private readonly IHumanConfigurationImportAdmission issuer;
    private readonly HumanConfigurationImportAuthority authority;
    private readonly Guid operationId;
    private readonly string digest;
    private readonly ImmutableArray<HumanConfigurationImportSettingEffect> effects;
    private readonly DbContextId contextId;
    private readonly RelationalConnectionIdentity connection;
    private readonly DbTransaction transaction;
    private readonly HumanImportPublicationQueries queries;
    private readonly string dataSource;
    private readonly string database;
    private readonly string namespaceIdentity;
    private readonly int? backend;
    private readonly string registry;
    private readonly PostgresNativeQueueEnrollment? enrollment;
    private Image expected = new([], []);
    private ImmutableArray<Plan> plans = [];
    private readonly List<Completion> completions = [];
    private bool compiled;
    private bool writing;
    private bool failed;

    private HumanConfigurationImportPublicationStore(ExploreDbContext context, ExploreDbContextExecutionLease lease,
        IServiceProvider services, HumanConfigurationImportAuthority authority, Guid operationId, string digest,
        ImmutableArray<HumanConfigurationImportSettingEffect> effects, DateTime occurredAt)
    {
        this.context = context;
        this.lease = lease;
        this.authority = authority;
        this.operationId = operationId;
        this.digest = digest;
        this.effects = effects;
        issuer = services.GetRequiredService<IHumanConfigurationImportAdmission>();
        contextId = context.ContextId;
        connection = RelationalConnectionIdentity.Capture(context);
        transaction = context.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new UnauthorizedAccessException("Original import observation requires its acquired physical attempt.");
        dataSource = connection.DataSource;
        database = connection.Database;
        backend = connection.BackendProcessId;
        queries = new(context, connection, transaction);
        namespaceIdentity = Table<SystemSetting>() + "\n" + Table<TenantSetting>();
        enrollment = lease.HumanScope?.Enrollment;
        registry = RegistryImage();
        OccurredAt = HumanImportPublicationQueries.StorageTimestamp(context, occurredAt);
        RequireSelection();
    }

    public DateTime OccurredAt { get; }

    internal static async Task<IHumanConfigurationImportPublicationStore> ObserveAsync(ExploreDbContext context,
        ExploreDbContextExecutionLease lease, IServiceProvider services, HumanConfigurationImportAuthority authority,
        Guid operationId, string digest, ImmutableArray<HumanConfigurationImportSettingEffect> effects,
        DateTime occurredAt, CancellationToken token)
    {
        if (!ReferenceEquals(context, services.GetRequiredService<ExploreDbContext>())
            || operationId == Guid.Empty || occurredAt.Kind != DateTimeKind.Utc || effects.IsDefaultOrEmpty
            || effects.Distinct().Count() != effects.Length
            || effects.Any(effect => !PublicationPolicySettingKeys.All.Contains(effect.Key, StringComparer.Ordinal)
                || effect.Scope != (effect.TenantId.HasValue ? SettingScope.Tenant : SettingScope.Instance)
                || authority.Target.TenantId.HasValue && effect.TenantId != authority.Target.TenantId
                || effect.Kind is not (HumanSettingsMutationKind.SetValue or HumanSettingsMutationKind.SetLock)
                || effect.Kind == HumanSettingsMutationKind.SetLock && (effect.TenantId.HasValue || effect.IsLocked != false)))
            throw new UnauthorizedAccessException("Only the actual import compiler's fixed publication observation is supported.");
        HumanSourceProviderOperations.RequirePostgresOrSqlite(context, "human_import_publication_provider_not_supported");
        var store = new HumanConfigurationImportPublicationStore(context, lease, services, authority,
            operationId, digest, effects, occurredAt);
        store.expected = await store.ReadImageAsync(token);
        store.RequireSelection();
        return store;
    }

    public async Task RequireCurrentAsync(CancellationToken cancellationToken)
    {
        RequireSelection();
        if (!Same(expected, await ReadImageAsync(cancellationToken)) || registry != RegistryImage())
            throw new UnauthorizedAccessException("Original import publication rows, absence or typed metadata changed.");
        RequireSelection();
    }

    public async Task CompileAsync(ImmutableArray<HumanConfigurationImportPublication> publications,
        CancellationToken cancellationToken)
    {
        if (compiled || publications.IsDefaultOrEmpty)
            throw new UnauthorizedAccessException("The original publication observation may only be compiled once.");
        await RequireCurrentAsync(cancellationToken);
        var all = ImmutableArray.CreateBuilder<HumanConfigurationImportSettingEffect>();
        var built = ImmutableArray.CreateBuilder<Plan>();
        Image before = expected;
        foreach (var publication in publications)
        {
            RequireCapsule(publication);
            await issuer.RequirePublicationAsync(publication, cancellationToken);
            foreach (var mutation in publication.Mutations)
            {
                all.Add(new(publication.TenantId.HasValue ? SettingScope.Tenant : SettingScope.Instance,
                    publication.TenantId, mutation.Key, HumanSettingsMutationKind.SetValue, mutation.JsonValue));
                if (mutation.IsLocked.HasValue)
                    all.Add(new(SettingScope.Instance, null, mutation.Key, HumanSettingsMutationKind.SetLock,
                        IsLocked: mutation.IsLocked));
            }
            var plan = Compile(publication, before);
            built.Add(plan);
            before = plan.After;
        }
        if (all.Count != effects.Length || all.Distinct().Count() != all.Count || effects.Any(effect => !all.Contains(effect)))
            throw new UnauthorizedAccessException("Accepted publication slices must consume exactly the original effects.");
        plans = built.ToImmutable();
        compiled = true;
    }

    public async Task<PublicationPolicyMutationSnapshot> ReadAsync(HumanConfigurationImportPublication publication,
        CancellationToken cancellationToken)
    {
        RequireNext(publication);
        await issuer.RequirePublicationAsync(publication, cancellationToken);
        return new(expected.Systems.Select(row => new PublicationPolicySystemValueSnapshot(row.Key, row.Value, row.Locked))
                .OrderBy(row => KeyOrder(row.Key)).ToImmutableArray(),
            expected.Tenants.Where(row => publication.TenantId is null || row.Tenant == publication.TenantId)
                .OrderBy(row => row.Tenant).ThenBy(row => KeyOrder(row.Key))
                .Select(row => new PublicationPolicyTenantValueSnapshot(row.Tenant, row.Key, row.Value)).ToImmutableArray());
    }

    public async Task<CoordinatedSettingMutationWriteResult> WriteAsync(HumanConfigurationImportPublication publication,
        CancellationToken cancellationToken)
    {
        Plan plan = RequireNext(publication);
        PublicationPolicyMutationBoundary.RequireImportWrite(publication);
        await issuer.RequirePublicationAsync(publication, cancellationToken);
        writing = true;
        try
        {
            await queries.WriteAsync(authority.Target.Scope, authority.Target.TenantId, enrollment,
                publication, plan.Before, plan.After, cancellationToken);
            if (!Same(plan.After, await ReadImageAsync(cancellationToken)))
                throw new UnauthorizedAccessException("The selected publication writer did not persist its exact postimage.");
            expected = plan.After;
            await issuer.RequirePublicationAsync(publication, cancellationToken);
            completions.Add(new(this, publication, transaction, plan.After));
            return new(plan.Changes);
        }
        catch { failed = true; throw; }
        finally { writing = false; }
    }

    public async Task RequireCompletedAsync(CancellationToken cancellationToken)
    {
        RequireSelection();
        if (!compiled || writing || completions.Count != plans.Length
            || completions.Where((completion, index) => !ReferenceEquals(completion.Writer, this)
                || !ReferenceEquals(completion.Publication, plans[index].Publication)
                || !ReferenceEquals(completion.Transaction, transaction) || !Same(completion.After, plans[index].After)).Any())
            throw new UnauthorizedAccessException("Every accepted import publication needs its actual private writer completion.");
        await RequireCurrentAsync(cancellationToken);
    }

    private void RequireCapsule(HumanConfigurationImportPublication publication)
    {
        RequireSelection();
        if (!ReferenceEquals(publication.Store, this) || !ReferenceEquals(publication.Subject, authority.Subject)
            || publication.OperationId != operationId || publication.Target != authority.Target || publication.ArtifactDigest != digest
            || authority.Target.TenantId.HasValue && publication.TenantId != authority.Target.TenantId
            || publication.Mutations.IsDefaultOrEmpty
            || publication.Mutations.Select(mutation => mutation.Key).Distinct(StringComparer.Ordinal).Count() != publication.Mutations.Length
            || publication.Mutations.Any(mutation => mutation.TenantId != publication.TenantId
                || mutation.Kind != PublicationPolicyMutationKind.Set || mutation.JsonValue is null
                || mutation.IsLocked != (publication.TenantId.HasValue ? null : false)))
            throw new UnauthorizedAccessException("The original actor, target, artifact and exact import slice are required.");
    }

    private Plan RequireNext(HumanConfigurationImportPublication publication)
    {
        RequireCapsule(publication);
        if (!compiled || writing || completions.Count >= plans.Length
            || !ReferenceEquals(plans[completions.Count].Publication, publication))
            throw new UnauthorizedAccessException("Publication slices cannot be omitted, replaced, duplicated or reordered.");
        return plans[completions.Count];
    }

    private void RequireSelection()
    {
        lease.RequireOriginalOwner(context);
        if (failed || !ReferenceEquals(lease.HumanScope?.Subject, authority.Subject)
            || !ReferenceEquals(lease.HumanLifetime, authority.Lifetime)
            || lease.HumanScope.Purpose != (authority.Target.Scope == ConfigurationImportScope.Instance
                ? HumanAdministratorCommitPurpose.ConfigurationImport : HumanAdministratorCommitPurpose.RuntimeTenant)
            || lease.SelectedTenantId != authority.Target.TenantId
            || lease.PolicyPurpose is not (null or Explore.Application.Contracts.Operations.PolicyMutationPurpose.Standard)
            || context.IsConfigurationInstanceImportOwner != (authority.Target.Scope == ConfigurationImportScope.Instance)
            || context.IsTenantFilterBypassed || context.ContextId != contextId
            || context.HumanCommitContextLease != contextId
            || !ReferenceEquals(context.HumanCommitAuditSubject, authority.Subject)
            || !connection.Matches(context) || connection.State != ConnectionState.Open
            || connection.DataSource != dataSource || connection.Database != database
            || connection.BackendProcessId is { } processId && processId != backend
            || !ReferenceEquals(transaction, context.Database.CurrentTransaction?.GetDbTransaction())
            || transaction.IsolationLevel != IsolationLevel.Serializable || !connection.Owns(transaction)
            || System.Transactions.Transaction.Current is not null
            || namespaceIdentity != Table<SystemSetting>() + "\n" + Table<TenantSetting>())
            throw new UnauthorizedAccessException("The original human import context, purpose, connector and transaction are required.");
    }

    private Plan Compile(HumanConfigurationImportPublication publication, Image before)
    {
        var systems = before.Systems.ToDictionary(row => row.Key, StringComparer.Ordinal);
        var tenants = before.Tenants.ToDictionary(row => (row.Tenant, row.Key));
        var changes = ImmutableArray.CreateBuilder<CoordinatedSettingValueChange>();
        foreach (var mutation in publication.Mutations.OrderBy(item => KeyOrder(item.Key)))
        {
            if (publication.TenantId is { } tenant)
            {
                tenants.TryGetValue((tenant, mutation.Key), out var old);
                if (old?.Value == mutation.JsonValue) continue;
                tenants[(tenant, mutation.Key)] = old is null
                    ? new(Guid.CreateVersion7(), tenant, mutation.Key, mutation.JsonValue!, false, OccurredAt.Ticks,
                        authority.Subject.PrincipalId, null, null)
                    : old with { Value = mutation.JsonValue!, Updated = OccurredAt.Ticks, Updater = authority.Subject.PrincipalId };
                changes.Add(new(mutation.Key, old?.Value, mutation.JsonValue));
            }
            else
            {
                var definition = SettingRegistry.Get(mutation.Key)!;
                systems.TryGetValue(mutation.Key, out var old);
                var next = new SystemRow(old?.Id ?? CanonicalId(mutation.Key), mutation.Key, mutation.JsonValue!, false,
                    (int)definition.ValueType, definition.AllowedValues is null ? null : JsonSerializer.Serialize(definition.AllowedValues),
                    definition.Description, definition.Category, KeyOrder(mutation.Key) + 1,
                    old?.Created ?? OccurredAt.Ticks, old is null ? authority.Subject.PrincipalId : old.Creator,
                    old is null ? null : OccurredAt.Ticks, old is null ? null : authority.Subject.PrincipalId);
                if (old is not null && next with { Updated = old.Updated, Updater = old.Updater } == old) continue;
                systems[mutation.Key] = next;
                changes.Add(new(mutation.Key, old?.Value, mutation.JsonValue));
            }
        }
        return new(publication, before, new(systems.Values.OrderBy(row => row.Key, StringComparer.Ordinal).ToImmutableArray(),
            tenants.Values.OrderBy(row => row.Tenant).ThenBy(row => row.Key, StringComparer.Ordinal).ToImmutableArray()),
            changes.ToImmutable());
    }

    private Task<Image> ReadImageAsync(CancellationToken token)
    {
        RequireSelection();
        return queries.ReadAsync(authority.Target.Scope, authority.Target.TenantId, enrollment, RequireSelection, token);
    }

    private string Table<T>()
    {
        var entity = context.Model.FindEntityType(typeof(T))!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
    }

    internal static bool Same(Image left, Image right) =>
        left.Systems.SequenceEqual(right.Systems) && left.Tenants.SequenceEqual(right.Tenants);
    private static int KeyOrder(string key) => PublicationPolicySettingKeys.All.ToList().IndexOf(key);
    private static string RegistryImage() => JsonSerializer.Serialize(PublicationPolicySettingKeys.All
        .Select(key => new { Key = key, Id = CanonicalId(key), Definition = SettingRegistry.Get(key) }));
    private static Guid CanonicalId(string key) => KeyOrder(key) switch
    {
        0 => SeedIds.SystemSettingEventReportingIntakeEnabledId,
        1 => SeedIds.SystemSettingRequireApprovalId,
        2 => SeedIds.SystemSettingUserSubmissionEnabledId,
        3 => SeedIds.SystemSettingOrgSubmissionEnabledId,
        4 => SeedIds.SystemSettingGroupSubmissionEnabledId,
        _ => throw new UnauthorizedAccessException("Unknown publication key.")
    };
    internal sealed record SystemRow(Guid Id, string Key, string Value, bool Locked, int Type, string? Allowed,
        string? Description, string? Category, int Order, long Created, Guid? Creator, long? Updated, Guid? Updater);
    internal sealed record TenantRow(Guid Id, Guid Tenant, string Key, string Value, bool Locked,
        long Created, Guid? Creator, long? Updated, Guid? Updater);
    internal sealed record Image(ImmutableArray<SystemRow> Systems, ImmutableArray<TenantRow> Tenants);
    private sealed record Plan(HumanConfigurationImportPublication Publication, Image Before, Image After,
        ImmutableArray<CoordinatedSettingValueChange> Changes);
    private sealed record Completion(HumanConfigurationImportPublicationStore Writer,
        HumanConfigurationImportPublication Publication, DbTransaction Transaction, Image After);
}
