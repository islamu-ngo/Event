using System.Collections.Immutable;
using System.Data;
using Explore.Domain;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Fixed current-source reads using the sealed Global import owner's existing connection and transaction.
/// </summary>
public static partial class PostgresConfigurationImportLegalSourceContract
{
    public const string OwnerRole = "event_configuration_import_legal_source_owner";
    public const string MigratorRole = "event_configuration_import_legal_source_migrator";
    public const string GlobalRole = "event_configuration_import_legal_source_global";
    private const string FunctionName = "configuration_import_legal_source";
    private const string PolicyName = "configuration_import_legal_source_read";

    public static async Task<IReadOnlyList<LegalDocumentPortableSource>> ReadAsync(
        ExploreDbContext context, LegalDocumentScope scope, Guid? tenantId, CancellationToken ct)
    {
        ValidateOwner(context);
        if (scope is not (LegalDocumentScope.Instance or LegalDocumentScope.Tenant)
            || scope == LegalDocumentScope.Instance && tenantId is not null
            || scope == LegalDocumentScope.Tenant && (tenantId is null || tenantId == Guid.Empty))
            throw new ArgumentException("Legal source scope and tenant are inconsistent.", nameof(tenantId));

        int maximumDocuments = Math.Min(
            LegalDocumentKindCatalog.Entries.Values.Count(entry => entry.Scope == scope),
            LegalMarkdownContentLimits.MaximumDocumentsPerScope);
        int maximumRows = maximumDocuments * LegalMarkdownContentLimits.MaximumLocalesPerDocument;
        bool openHere = context.Database.GetDbConnection().State != ConnectionState.Open;
        if (openHere)
            await context.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = $"""
                SELECT document_id, kind, audience, requires_fresh_acceptance,
                    template_id, template_version, template_source_kind,
                    template_license_expression, template_review_reference,
                    language_tag, title, summary, markdown
                FROM {FunctionSql(context)}(@scope, @tenant)
                """;
            command.Parameters.Add(new NpgsqlParameter("scope", NpgsqlDbType.Integer) { Value = (int)scope });
            command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid)
            { Value = tenantId is { } id ? id : DBNull.Value });
            if (context.Database.GetCommandTimeout() is { } timeout)
                command.CommandTimeout = timeout;
            var rows = new List<SourceRow>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (rows.Count >= maximumRows)
                    throw new InvalidOperationException("configuration_import_legal_source_invalid");
                rows.Add(new SourceRow(
                    reader.GetGuid(0), (LegalDocumentKind)reader.GetInt32(1),
                    (LegalDocumentAudience)reader.GetInt32(2), reader.GetBoolean(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : (LegalDocumentTemplateSourceKind)reader.GetInt32(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    new LegalDocumentPortableLocalization(
                        reader.GetString(9), reader.GetString(10), reader.GetString(11), reader.GetString(12))));
            }

            var documents = rows.GroupBy(row => row.DocumentId).ToArray();
            if (documents.Length > maximumDocuments
                || documents.Select(document => document.First().Kind).Distinct().Count() != documents.Length
                || documents.Any(document => LegalDocumentKindCatalog.Get(document.First().Kind).Scope != scope))
                throw new InvalidOperationException("configuration_import_legal_source_invalid");
            return documents.Select(document =>
            {
                SourceRow first = document.First();
                LegalDocumentTemplateProvenance? provenance =
                    first.TemplateId is null && first.TemplateVersion is null && first.TemplateSourceKind is null
                        && first.TemplateLicenseExpression is null && first.TemplateReviewReference is null
                    ? null
                    : LegalDocumentTemplateProvenance.Create(
                        first.TemplateId!, first.TemplateVersion!,
                        first.TemplateSourceKind ?? throw new InvalidOperationException("configuration_import_legal_source_invalid"),
                        first.TemplateLicenseExpression!, first.TemplateReviewReference!);
                return new LegalDocumentPortableSource(
                    first.Kind, first.Audience, first.RequiresFreshAcceptance, provenance,
                    document.Select(row => row.Localization)
                        .OrderBy(localization => localization.LanguageTag, StringComparer.Ordinal)
                        .ToImmutableArray());
            }).OrderBy(document => document.Kind).ToArray();
        }
        finally
        {
            if (openHere)
                await context.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Checks the same session purpose, role graph, ACL, policy and function fingerprints as the native reader.
    /// Does not install or enroll authority.
    /// </summary>
    public static async Task ValidateReadinessAsync(ExploreDbContext context, CancellationToken ct)
    {
        ValidateOwner(context);
        bool openHere = context.Database.GetDbConnection().State != ConnectionState.Open;
        if (openHere)
            await context.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText =
                $"SELECT count(*) FROM {FunctionSql(context)}({(int)LegalDocumentScope.Instance}, NULL::uuid)";
            if (context.Database.GetCommandTimeout() is { } timeout)
                command.CommandTimeout = timeout;
            await command.ExecuteScalarAsync(ct);
        }
        finally
        {
            if (openHere)
                await context.Database.CloseConnectionAsync();
        }
    }

    private static void ValidateOwner(ExploreDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ValidateTenantLease();
        if (!context.Database.IsNpgsql() || !context.IsConfigurationInstanceImportOwner
            || context.TenantFilterTenantId is not null)
            throw new InvalidOperationException("Legal source reads require the sealed Global instance-import owner.");
    }

    private sealed record SourceRow(
        Guid DocumentId, LegalDocumentKind Kind, LegalDocumentAudience Audience,
        bool RequiresFreshAcceptance, string? TemplateId, string? TemplateVersion,
        LegalDocumentTemplateSourceKind? TemplateSourceKind, string? TemplateLicenseExpression,
        string? TemplateReviewReference, LegalDocumentPortableLocalization Localization);
}
