using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Persists only Domain-created imported drafts and revisions on the immutable Global
/// import owner. Reads materialize the complete mapped aggregate; native writes use
/// the same transaction and never replace historical versions or publication evidence.
/// </summary>
public static partial class PostgresConfigurationImportLegalMutationContract
{
    public const string OwnerRole = "event_configuration_import_legal_mutation_owner";
    public const string MigratorRole = "event_configuration_import_legal_mutation_migrator";
    public const string GlobalRole = "event_configuration_import_legal_mutation_global";

    private static readonly ConditionalWeakTable<ExploreDbContext, Dictionary<Target, ReadOrigin>> Reads = new();

    public static async Task<LegalDocument?> ReadForUpdateAsync(
        ExploreDbContext context, LegalDocumentScope scope, Guid? tenantId,
        LegalDocumentKind kind, CancellationToken ct)
    {
        DbTransaction transaction = ValidateOwner(context);
        Target target = ValidateTarget(scope, tenantId, kind);
        if (context.ChangeTracker.Entries<LegalDocument>().Any(entry =>
                entry.Entity.Scope == scope && entry.Entity.TenantId == tenantId && entry.Entity.Kind == kind))
            throw new InvalidOperationException("Legal import targets must be read before tracking or mutation.");
        HashSet<object> previouslyTracked = context.ChangeTracker.Entries()
            .Where(entry => entry.Entity is LegalDocument or LegalDocumentVersion
                or LegalDocumentLocalizedSource or LegalDocumentPublication)
            .Select(entry => entry.Entity).ToHashSet(ReferenceEqualityComparer.Instance);

        List<LegalDocument> roots = await ReadRowsAsync<LegalDocument>(context, "read_root", target, ct);
        LegalDocument? document = roots.SingleOrDefault();
        if (document is not null)
        {
            List<LegalDocumentVersion> versions =
                await ReadRowsAsync<LegalDocumentVersion>(context, "read_versions", target, ct);
            List<LegalDocumentLocalizedSource> sources =
                await ReadRowsAsync<LegalDocumentLocalizedSource>(context, "read_sources", target, ct);
            List<LegalDocumentPublication> publications =
                await ReadRowsAsync<LegalDocumentPublication>(context, "read_publications", target, ct);
            if (document.Scope != scope || document.TenantId != tenantId || document.Kind != kind
                || document.Versions.Count != versions.Count || document.Publications.Count != publications.Count
                || versions.Count != document.CurrentVersion
                || !versions.Select(version => version.Version).Order().SequenceEqual(
                    Enumerable.Range(1, document.CurrentVersion))
                || versions.Any(version => version.LegalDocumentId != document.Id
                    || !ReferenceEquals(version.LegalDocument, document)
                    || version.Sources.Count is < 1 or > LegalMarkdownContentLimits.MaximumLocalesPerDocument)
                || sources.Count != versions.Sum(version => version.Sources.Count)
                || sources.Any(source => !versions.Any(version =>
                    version.Id == source.LegalDocumentVersionId
                    && ReferenceEquals(source.LegalDocumentVersion, version)
                    && version.Sources.Contains(source)))
                || publications.Any(publication => publication.LegalDocumentId != document.Id
                    || !ReferenceEquals(publication.LegalDocument, document)
                    || !versions.Any(version => version.Id == publication.LegalDocumentVersionId
                        && ReferenceEquals(publication.LegalDocumentVersion, version))))
                throw new InvalidOperationException("configuration_import_legal_aggregate_invalid");

            context.Entry(document).Collection(item => item.Versions).IsLoaded = true;
            context.Entry(document).Collection(item => item.Publications).IsLoaded = true;
            foreach (LegalDocumentVersion version in versions)
                context.Entry(version).Collection(item => item.Sources).IsLoaded = true;
        }

        EntityEntry[] graph = document is null ? [] : Graph(context, document);
        if (graph.Any(entry => entry.State != EntityState.Unchanged || previouslyTracked.Contains(entry.Entity)))
            throw new InvalidOperationException("Legal import requires an unmodified materialized aggregate.");
        Reads.GetOrCreateValue(context)[target] = new ReadOrigin(
            context.Database.GetDbConnection(), transaction, document,
            graph.Select(entry => new OriginalEntity(entry.Entity, entry.CurrentValues.Clone())).ToArray(),
            document?.ConcurrencyStamp, document?.CurrentVersion, document?.State);
        return document;
    }

    public static async Task WriteAsync(
        ExploreDbContext context, LegalDocument document, CancellationToken ct)
    {
        DbTransaction transaction = ValidateOwner(context);
        ArgumentNullException.ThrowIfNull(document);
        Target target = ValidateTarget(document.Scope, document.TenantId, document.Kind);
        if (!Reads.TryGetValue(context, out Dictionary<Target, ReadOrigin>? reads)
            || !reads.TryGetValue(target, out ReadOrigin? origin)
            || !ReferenceEquals(origin.Connection, context.Database.GetDbConnection())
            || !ReferenceEquals(origin.Transaction, transaction))
            throw new InvalidOperationException("Legal import must read and write on the same owner transaction.");

        context.ChangeTracker.DetectChanges();
        LegalDocumentVersion version = ValidateMutation(context, document, origin);
        Guid stamp = Guid.CreateVersion7();
        Dictionary<string, object?> rootValues = MappedValues(context, document);
        rootValues[ColumnName(context, typeof(LegalDocument), nameof(LegalDocument.ConcurrencyStamp))] = stamp;
        await using DbCommand command = origin.Connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT {FunctionSql(context, "write")}(
                @scope, @tenant, @kind, @document, @version, @sources,
                @expected_stamp, @expected_version, @expected_state)
            """;
        command.Parameters.Add(Parameter("scope", NpgsqlDbType.Integer, (int)target.Scope));
        command.Parameters.Add(Parameter("tenant", NpgsqlDbType.Uuid, target.TenantId));
        command.Parameters.Add(Parameter("kind", NpgsqlDbType.Integer, (int)target.Kind));
        command.Parameters.Add(Parameter("document", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(rootValues)));
        command.Parameters.Add(Parameter("version", NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(MappedValues(context, version))));
        command.Parameters.Add(Parameter("sources", NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(version.Sources.OrderBy(source => source.LanguageTag, StringComparer.Ordinal)
                .Select(source => MappedValues(context, source)))));
        command.Parameters.Add(Parameter("expected_stamp", NpgsqlDbType.Uuid, origin.Stamp));
        command.Parameters.Add(Parameter("expected_version", NpgsqlDbType.Integer, origin.Version));
        command.Parameters.Add(Parameter("expected_state", NpgsqlDbType.Integer,
            origin.State is { } state ? (int)state : null));
        if (context.Database.GetCommandTimeout() is { } timeout)
            command.CommandTimeout = timeout;
        await command.ExecuteNonQueryAsync(ct);

        document.ConcurrencyStamp = stamp;
        // Accept only this graph, never unrelated import state. EF fixup keeps the
        // actual instances and history; subsequent SaveChanges has no private DML.
        foreach (EntityEntry entry in Graph(context, document))
            entry.State = EntityState.Unchanged;
        reads.Remove(target);
    }

    private static async Task<List<T>> ReadRowsAsync<T>(
        ExploreDbContext context, string operation, Target target, CancellationToken ct) where T : class =>
        await context.Set<T>()
            .FromSqlRaw($"SELECT * FROM {FunctionSql(context, operation)}(@scope, @tenant, @kind)",
                Parameter("scope", NpgsqlDbType.Integer, (int)target.Scope),
                Parameter("tenant", NpgsqlDbType.Uuid, target.TenantId),
                Parameter("kind", NpgsqlDbType.Integer, (int)target.Kind))
            .IgnoreQueryFilters([QueryFilterNames.Tenant])
            .AsTracking().ToListAsync(ct);

    private static LegalDocumentVersion ValidateMutation(
        ExploreDbContext context, LegalDocument document, ReadOrigin origin)
    {
        bool inserted = origin.Document is null;
        LegalDocumentKindDescriptor descriptor = LegalDocumentKindCatalog.Get(document.Kind);
        RequireUuid(document.Id);
        RequireUuid(document.ConcurrencyStamp);
        RequireUtc(document.CreatedAt);
        if (document.CreatedBy is { } createdBy) RequireUuid(createdBy);
        if (document.UpdatedBy is { } updatedBy) RequireUuid(updatedBy);
        if (document.State != LegalDocumentLifecycleState.ReviewRequired
            || document.AccountableIdentityReference is not null
            || document.OwnerRole != descriptor.OwnerRole
            || document.AuthorityKey != AuthorityKey(document.Scope, document.TenantId)
            || document.CurrentVersion != (inserted ? 1 : checked(origin.Version!.Value + 1))
            || document.Versions.Count != document.CurrentVersion
            || !inserted && (!ReferenceEquals(document, origin.Document)
                || origin.State is not (LegalDocumentLifecycleState.Published or LegalDocumentLifecycleState.Retired)
                || document.ConcurrencyStamp != origin.Stamp)
            || inserted && (document.Publications.Count != 0 || document.UpdatedAt is not null || document.UpdatedBy is not null))
            throw new InvalidOperationException("Only a new imported draft or imported revision may be persisted.");

        LegalDocumentVersion version = document.Versions.Single(item => item.Version == document.CurrentVersion);
        RequireUuid(version.Id);
        RequireUtc(version.CreatedAt);
        if (version.LegalDocumentId != document.Id
            || version.State != LegalDocumentLifecycleState.ReviewRequired
            || !Enum.IsDefined(version.Audience)
            || version.SourceOrigin is not { Length: 71 } sourceOrigin
            || !sourceOrigin.StartsWith("sha256:", StringComparison.Ordinal)
            || sourceOrigin.AsSpan(7).ContainsAnyExcept("0123456789abcdef")
            || version.ReviewerId is not null || version.ReviewEvidenceReference is not null
            || version.AccountableIdentityReference is not null || version.ApprovedAt is not null
            || version.ProposedEffectiveAt is not null || version.PublishedAt is not null || version.RetiredAt is not null
            || (inserted ? document.CreatedAt != version.CreatedAt : document.UpdatedAt != version.CreatedAt)
            || version.Sources.Count is < 1 or > LegalMarkdownContentLimits.MaximumLocalesPerDocument
            || version.Sources.Select(source => source.LanguageTag).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != version.Sources.Count)
            throw new InvalidOperationException("configuration_import_legal_revision_invalid");
        if (document.UpdatedAt is { } updatedAt) RequireUtc(updatedAt);

        if (new object?[] { version.TemplateId, version.TemplateVersion, version.TemplateSourceKind,
                version.TemplateLicenseExpression, version.TemplateReviewReference }.Any(value => value is not null))
        {
            LegalDocumentTemplateProvenance provenance = LegalDocumentTemplateProvenance.Create(
                version.TemplateId!, version.TemplateVersion!,
                version.TemplateSourceKind ?? throw new InvalidOperationException("Missing template provenance."),
                version.TemplateLicenseExpression!, version.TemplateReviewReference!);
            if (provenance.TemplateId != version.TemplateId || provenance.TemplateVersion != version.TemplateVersion
                || provenance.LicenseExpression != version.TemplateLicenseExpression
                || provenance.ReviewReference != version.TemplateReviewReference)
                throw new InvalidOperationException("Noncanonical template provenance.");
        }

        foreach (LegalDocumentLocalizedSource source in version.Sources)
        {
            RequireUuid(source.Id);
            LegalDocumentLocalizedSource validated = LegalDocumentLocalizedSource.Create(
                source.LanguageTag, source.Title, source.Summary, source.Markdown);
            if (source.LegalDocumentVersionId != version.Id
                || !ReferenceEquals(source.LegalDocumentVersion, version)
                || source.LanguageTag != validated.LanguageTag || source.Title != validated.Title
                || source.Summary != validated.Summary || source.Markdown != validated.Markdown
                || source.Utf8ByteCount != validated.Utf8ByteCount || source.LinkCount != validated.LinkCount
                || source.PlaceholderCount != validated.PlaceholderCount)
                throw new InvalidOperationException("configuration_import_legal_source_invalid");
        }

        var digest = new StringBuilder();
        void Append(string value) => digest.Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':').Append(value).Append(';');
        Append(((int)version.Audience).ToString(CultureInfo.InvariantCulture));
        Append(version.SourceOrigin!);
        Append(version.RequiresFreshAcceptance ? "1" : "0");
        Append(version.TemplateId ?? "");
        Append(version.TemplateVersion ?? "");
        Append(version.TemplateSourceKind?.ToString() ?? "");
        Append(version.TemplateLicenseExpression ?? "");
        Append(version.TemplateReviewReference ?? "");
        foreach (LegalDocumentLocalizedSource source in version.Sources.OrderBy(item => item.LanguageTag, StringComparer.Ordinal))
        {
            Append(source.LanguageTag);
            Append(source.Title);
            Append(source.Summary);
            Append(source.Markdown);
        }
        if (version.ContentDigest != Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(digest.ToString()))))
            throw new InvalidOperationException("configuration_import_legal_digest_invalid");

        EntityEntry[] graph = Graph(context, document);
        if (graph.Any(entry => entry.State == EntityState.Deleted)
            || graph.Select(entry => entry.Entity).Distinct(ReferenceEqualityComparer.Instance).Count() != graph.Length
            || graph.Length != origin.Graph.Length + (inserted ? 1 : 0) + 1 + version.Sources.Count)
            throw new InvalidOperationException("Legal import cannot remove or substitute aggregate history.");
        foreach (OriginalEntity original in origin.Graph)
        {
            EntityEntry entry = context.Entry(original.Entity);
            if (!graph.Any(item => ReferenceEquals(item.Entity, original.Entity))
                || entry.State is EntityState.Detached or EntityState.Added or EntityState.Deleted)
                throw new InvalidOperationException("Legal import cannot replace aggregate history.");
            foreach (var property in original.Values.Properties)
            {
                if (ReferenceEquals(original.Entity, document) && RootUpdates.Contains(property.Name, StringComparer.Ordinal))
                    continue;
                if (!Equals(original.Values[property], entry.CurrentValues[property])
                    || !Equals(original.Values[property], entry.OriginalValues[property]))
                    throw new InvalidOperationException("Legal import cannot rewrite aggregate history.");
            }
        }
        return version;
    }

    private static EntityEntry[] Graph(ExploreDbContext context, LegalDocument document) =>
        new object[] { document }.Concat(document.Versions)
            .Concat(document.Versions.SelectMany(version => version.Sources))
            .Concat(document.Publications).Select(entity => context.Entry(entity)).ToArray();

    private static Dictionary<string, object?> MappedValues(ExploreDbContext context, object entity) =>
        context.Entry(entity).Properties.ToDictionary(
            property => ColumnName(context, entity.GetType(), property.Metadata.Name),
            property => property.CurrentValue is Enum value
                ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : property.CurrentValue,
            StringComparer.Ordinal);

    private static Target ValidateTarget(LegalDocumentScope scope, Guid? tenantId, LegalDocumentKind kind)
    {
        if (LegalDocumentKindCatalog.Get(kind).Scope != scope
            || scope == LegalDocumentScope.Instance && tenantId is not null
            || scope == LegalDocumentScope.Tenant && (tenantId is null || tenantId == Guid.Empty))
            throw new ArgumentException("Legal import target does not match the fixed kind authority.");
        return new Target(scope, tenantId, kind);
    }

    private static string AuthorityKey(LegalDocumentScope scope, Guid? tenantId) =>
        scope == LegalDocumentScope.Instance ? "instance" : $"tenant:{tenantId!.Value:N}";

    private static DbTransaction ValidateOwner(ExploreDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ValidateTenantLease();
        if (!context.Database.IsNpgsql() || !context.IsConfigurationInstanceImportOwner
            || context.TenantFilterTenantId is not null || context.Database.CurrentTransaction is null
            || context.Database.GetDbConnection().State != ConnectionState.Open)
            throw new InvalidOperationException("Legal import requires the sealed tenant-null Global owner and its active transaction.");
        DbTransaction transaction = context.Database.CurrentTransaction.GetDbTransaction();
        if (!ReferenceEquals(transaction.Connection, context.Database.GetDbConnection()))
            throw new InvalidOperationException("Legal import transaction connection changed.");
        return transaction;
    }

    private static void RequireUuid(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("A nonempty UUID is required.");
    }

    private static void RequireUtc(DateTime value)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A nondefault UTC timestamp is required.");
    }

    private static NpgsqlParameter Parameter(string name, NpgsqlDbType type, object? value) =>
        new(name, type) { Value = value ?? DBNull.Value };

    private sealed record Target(LegalDocumentScope Scope, Guid? TenantId, LegalDocumentKind Kind);
    private sealed record OriginalEntity(object Entity, PropertyValues Values);
    private sealed record ReadOrigin(
        DbConnection Connection, DbTransaction Transaction, LegalDocument? Document,
        OriginalEntity[] Graph, Guid? Stamp, int? Version, LegalDocumentLifecycleState? State);
}
