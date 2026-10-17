using System.Data;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Models.Storage;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Database.ProviderPrimitives;

/// <summary>
/// Materializes only the native capability's bounded private graph into the
/// existing domain model. Domain mutations remain in the repository.
/// </summary>
internal sealed class AtprotoMaterializationProviderOperations : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly ExploreDbContext _context;
    private readonly string _scope;
    private JsonDocument _graph;

    private AtprotoMaterializationProviderOperations(
        ExploreDbContext context, string scope, JsonDocument graph)
    {
        _context = context;
        _scope = scope;
        _graph = graph;
        LoadEntities();
    }

    internal IReadOnlyList<Guid> AffectedTenants =>
        _graph.RootElement.GetProperty("tenants").EnumerateArray().Select(value => value.GetGuid()).ToArray();

    internal static Task<AtprotoMaterializationProviderOperations> CreateAsync(
        ExploreDbContext context, AtprotoPdsSnapshotApplyRequest request, CancellationToken cancellationToken) =>
        CreateAsync(context, Scope(request.Claim, "snapshot", request.ScannedDids,
            request.Snapshots.SelectMany(snapshot => snapshot.Items).Select(item => item.Record),
            request.PresentationTenantIds, request.EventImports, request.ObservedAt), cancellationToken);

    internal static Task<AtprotoMaterializationProviderOperations> CreateAsync(
        ExploreDbContext context, AtprotoJetstreamApplyRequest request, CancellationToken cancellationToken)
    {
        IEnumerable<AtprotoRecord> records = request.Record is { } record ? [record] : [];
        if (request.EventProjectionInvalidation is { } invalidation)
            records = [new AtprotoRecord
            {
                Did = invalidation.Did, Collection = invalidation.Collection, RecordKey = invalidation.RecordKey
            }];
        string kind = request.AccountPurge is not null ? "purge"
            : records.Any() ? "record" : "quarantine";
        string[] dids = request.AccountPurge is { } purge ? [purge.Did] : records.Select(value => value.Did).ToArray();
        return CreateAsync(context, Scope(request.Claim, kind, dids, records,
            request.Presentations.Select(value => value.TenantId), request.EventImports, request.ObservedAt),
            cancellationToken);
    }

    private static async Task<AtprotoMaterializationProviderOperations> CreateAsync(
        ExploreDbContext context, string scope, CancellationToken cancellationToken)
    {
        RequireTransaction(context);
        string graph = await ExecuteAsync(context, "read", scope, null, cancellationToken);
        return new(context, scope, JsonDocument.Parse(graph));
    }

    internal IQueryable<TEntity> Query<TEntity>() where TEntity : class =>
        _context.Set<TEntity>().Local.AsQueryable();

    internal async Task FenceStorageAsync(CancellationToken cancellationToken)
    {
        string graph = await ExecuteAsync(_context, "fence_storage", _scope, null, cancellationToken);
        _graph.Dispose();
        _graph = JsonDocument.Parse(graph);
        LoadEntities();
    }

    internal async Task SaveAsync(
        IReadOnlyCollection<StagedStorageWrite> consumed,
        IReadOnlyCollection<(Guid TenantId, Guid ObjectId)> retiring,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        RequireTransaction(_context);
        _context.DisclosureMutations.Enlist(AffectedTenants);
        var retirements = new List<object>();
        foreach (var target in retiring.OrderBy(value => value.TenantId).ThenBy(value => value.ObjectId))
        {
            StorageObject source = Query<StorageObject>().SingleOrDefault(value =>
                value.Id == target.ObjectId && value.TenantId == target.TenantId)
                ?? throw new InvalidOperationException("Federation thumbnail retirement requires exact retained custody.");
            StorageUploadSession[] sessions = Query<StorageUploadSession>()
                .Where(value => value.Id == target.ObjectId || value.StorageObjectId == target.ObjectId).ToArray();
            StorageProducerOperation? operation = Query<StorageProducerOperation>()
                .SingleOrDefault(value => value.Id == target.ObjectId);
            StorageObjectDeletionTombstone custody;
            try
            {
                custody = StorageRetirementTarget.Capture(source, sessions, operation, observedAt);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidOperationException(
                    "Federation thumbnail retirement requires exact retained custody.", exception);
            }
            retirements.Add(new
            {
                id = source.Id,
                tenant = source.TenantId,
                target = Row(_context.Entry(custody), original: false)
            });
        }

        // Preparation happens once for the whole graph, including shared entries.
        // The prepared-save seam retains all existing source/storage hooks without
        // issuing a second audit/concurrency preparation pass.
        _context.PrepareTrackedEntities();
        _context.ChangeTracker.DetectChanges();
        var privateTypes = PostgresAtprotoMaterializationContract.PrivateRelations
            .ToDictionary(relation => relation.Type, relation => relation.Name);
        EntityEntry[] entries = _context.ChangeTracker.Entries()
            .Where(entry => privateTypes.ContainsKey(entry.Metadata.ClrType)
                && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        var changes = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var group in entries.GroupBy(entry => privateTypes[entry.Metadata.ClrType]))
            changes[group.Key] = group.Select(entry => new
            {
                operation = entry.State switch
                {
                    EntityState.Added => "insert",
                    EntityState.Deleted => "delete",
                    _ => "update"
                },
                original = Row(entry, original: true),
                current = Row(entry, original: false)
            }).ToArray();
        changes["retirements"] = retirements;
        changes["accounting"] = consumed.Select(value => new
        {
            tenant = value.TenantId,
            provider = value.Write.Provider
        }).Distinct().ToArray();
        EntityEntry[] removed = entries.Where(entry => entry.State == EntityState.Deleted).ToArray();
        foreach (EntityEntry entry in entries)
            entry.State = EntityState.Unchanged;
        await _context.SavePreparedAtprotoChangesAsync(cancellationToken);
        string graph = await ExecuteAsync(_context, "apply", _scope,
            JsonSerializer.Serialize(changes, JsonOptions), cancellationToken);
        foreach (EntityEntry entry in removed)
            entry.State = EntityState.Detached;
        _graph.Dispose();
        _graph = JsonDocument.Parse(graph);
    }

    private void LoadEntities()
    {
        var materializers = _context.GetService<IStructuralTypeMaterializerSource>();
        foreach (var (name, type) in PostgresAtprotoMaterializationContract.PrivateRelations)
        {
            var entity = _context.Model.FindEntityType(type)!;
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var properties = entity.GetProperties().ToArray();
            var materializer = materializers.GetMaterializer(entity);
            foreach (JsonElement row in _graph.RootElement.GetProperty(name).EnumerateArray())
            {
                object?[] values = new object?[properties.Max(property => property.GetIndex()) + 1];
                foreach (var property in properties)
                {
                    string? column = property.GetColumnName(store);
                    if (column is null || !row.TryGetProperty(column, out JsonElement value)
                        || value.ValueKind == JsonValueKind.Null)
                        continue;
                    var converter = property.GetTypeMapping().Converter;
                    object? parsed = value.Deserialize(converter?.ProviderClrType ?? property.ClrType, JsonOptions);
                    values[property.GetIndex()] = converter is null ? parsed : converter.ConvertFromProvider(parsed);
                }
                EntityEntry? existing = _context.ChangeTracker.Entries().FirstOrDefault(entry =>
                    entry.Metadata.ClrType == type && entity.FindPrimaryKey()!.Properties.All(property =>
                        Equals(entry.Property(property.Name).CurrentValue, values[property.GetIndex()])));
                if (existing is not null)
                {
                    if (existing.State != EntityState.Unchanged)
                        throw new InvalidOperationException("Private materialization reload requires an unchanged graph.");
                    foreach (var property in properties)
                    {
                        existing.Property(property.Name).CurrentValue = values[property.GetIndex()];
                        existing.Property(property.Name).OriginalValue = values[property.GetIndex()];
                    }
                    existing.State = EntityState.Unchanged;
                    continue;
                }
                object instance = materializer(new MaterializationContext(new ValueBuffer(values), _context));
                _context.Attach(instance);
            }
        }
    }

    private static Dictionary<string, object?> Row(EntityEntry entry, bool original)
    {
        var store = StoreObjectIdentifier.Table(entry.Metadata.GetTableName()!, entry.Metadata.GetSchema());
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in entry.Metadata.GetProperties())
        {
            string? column = property.GetColumnName(store);
            if (column is null || property.GetComputedColumnSql() is not null)
                continue;
            object? value = original ? entry.OriginalValues[property.Name] : entry.CurrentValues[property.Name];
            var converter = property.GetTypeMapping().Converter;
            result[column] = converter is null ? value : converter.ConvertToProvider(value);
        }
        return result;
    }

    private static string Scope(
        AtprotoJetstreamClaim claim, string kind, IEnumerable<string> dids, IEnumerable<AtprotoRecord> records,
        IEnumerable<Guid> presentations, IReadOnlyList<AtprotoFederatedEventImportPlan> imports, DateTime observedAt)
    {
        AtprotoRecord[] inputs = records.ToArray();
        AtprotoFederatedEventImportPlan[] accepted = imports.Where(import => inputs.Any(record =>
            record.Did == import.Did && record.Uri == import.AtUri)).ToArray();
        return JsonSerializer.Serialize(new
        {
            kind,
            consumer = claim.ConsumerStateId,
            service = claim.Service,
            token = claim.LeaseToken,
            fence = claim.LeaseFence,
            observedAt,
            dids = dids.Distinct().ToArray(),
            records = inputs.Select(record => new { did = record.Did, collection = record.Collection, key = record.RecordKey }),
            presentations = presentations.Distinct().ToArray(),
            imports = accepted.Select(import => new { tenant = import.TenantId, did = import.Did, uri = import.AtUri }),
            receipts = accepted.Where(import => import.StagedThumbnail is not null).Select(import => new
            {
                id = import.StagedThumbnail!.OperationId,
                tenant = import.StagedThumbnail.TenantId,
                did = import.Did,
                uri = import.AtUri
            })
        }, JsonOptions);
    }

    private static async Task<string> ExecuteAsync(
        ExploreDbContext context, string operation, string scope, string? changes,
        CancellationToken cancellationToken)
    {
        RequireTransaction(context);
        var parameters = new List<object>
        {
            new NpgsqlParameter("scope", NpgsqlDbType.Jsonb) { Value = scope }
        };
        string arguments = "@scope";
        if (changes is not null)
        {
            arguments += ", @changes";
            parameters.Add(new NpgsqlParameter("changes", NpgsqlDbType.Jsonb) { Value = changes });
        }
        string query = $"SELECT {PostgresAtprotoMaterializationContract.FunctionSql(context, operation)}({arguments})::text AS \"Value\"";
        return await context.Database.SqlQueryRaw<string>(query, parameters.ToArray())
            .SingleAsync(cancellationToken);
    }

    private static void RequireTransaction(ExploreDbContext context)
    {
        if (!context.Database.IsNpgsql()
            || context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("ATProto private materialization requires the owned Serializable transaction.");
    }

    public void Dispose()
    {
        _graph.Dispose();
        GC.SuppressFinalize(this);
    }
}
