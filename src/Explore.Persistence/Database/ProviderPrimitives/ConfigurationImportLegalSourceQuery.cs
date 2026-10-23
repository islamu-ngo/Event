namespace Explore.Persistence.Database.ProviderPrimitives;

using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Security;
using Explore.Persistence.QueryFilters;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.EntityFrameworkCore;

internal static class ConfigurationImportLegalSourceQuery
{
    internal static async Task<IReadOnlyList<LegalDocumentPortableSource>> ReadAsync(
        ExploreDbContext context, LegalDocumentScope scope, Guid? tenantId, string authorityKey, CancellationToken ct)
    {
        if (context.Database.IsNpgsql() && context.IsConfigurationInstanceImportOwner)
            return await PostgresConfigurationImportLegalSourceContract.ReadAsync(context, scope, tenantId, ct);

        LegalDocumentKind[] kinds = LegalDocumentKindCatalog.Entries.Values
            .Where(entry => entry.Scope == scope).Select(entry => entry.Kind).Order().ToArray();
        int maximumDocuments = Math.Min(kinds.Length, LegalMarkdownContentLimits.MaximumDocumentsPerScope);
        var documentQuery = (
            from document in context.LegalDocuments.AsNoTracking()
            where document.Scope == scope && document.TenantId == tenantId
                && document.AuthorityKey == authorityKey && kinds.Contains(document.Kind)
            join version in context.LegalDocumentVersions.AsNoTracking()
                on new { DocumentId = document.Id, Version = document.CurrentVersion }
                equals new { DocumentId = version.LegalDocumentId, version.Version } into current
            from version in current.DefaultIfEmpty()
            orderby document.Kind, document.Id
            select new
            {
                document.Kind,
                VersionId = (Guid?)version.Id,
                version.Audience,
                version.RequiresFreshAcceptance,
                version.TemplateId,
                version.TemplateVersion,
                version.TemplateSourceKind,
                version.TemplateLicenseExpression,
                version.TemplateReviewReference
            });
        if (context.IsConfigurationInstanceImportOwner)
            documentQuery = documentQuery.IgnoreQueryFilters([QueryFilterNames.Tenant]);
        var documents = await documentQuery.Take(maximumDocuments + 1).ToListAsync(ct);
        if (documents.Count > maximumDocuments
            || documents.Any(document => document.VersionId is null)
            || documents.Select(document => document.Kind).Distinct().Count() != documents.Count)
            throw new InvalidOperationException("configuration_import_legal_source_invalid");

        Guid[] versionIds = documents.Select(document => document.VersionId!.Value).ToArray();
        var sourceQuery = context.LegalDocumentLocalizedSources.AsNoTracking();
        if (context.IsConfigurationInstanceImportOwner)
            sourceQuery = sourceQuery.IgnoreQueryFilters([QueryFilterNames.Tenant]);
        var sources = await sourceQuery
            .Where(source => versionIds.Contains(source.LegalDocumentVersionId))
            .OrderBy(source => source.LegalDocumentVersionId).ThenBy(source => source.LanguageTag)
            .Select(source => new
            {
                source.LegalDocumentVersionId,
                LanguageTag = source.LanguageTag.Length > LegalMarkdownContentLimits.MaximumLanguageTagLength
                    ? source.LanguageTag.Substring(0, LegalMarkdownContentLimits.MaximumLanguageTagLength + 1)
                    : source.LanguageTag,
                Title = source.Title.Length > LegalMarkdownContentLimits.MaximumTitleLength
                    ? source.Title.Substring(0, LegalMarkdownContentLimits.MaximumTitleLength + 1)
                    : source.Title,
                Summary = source.Summary.Length > LegalMarkdownContentLimits.MaximumSummaryLength
                    ? source.Summary.Substring(0, LegalMarkdownContentLimits.MaximumSummaryLength + 1)
                    : source.Summary,
                Markdown = source.Markdown.Length > LegalMarkdownContentLimits.MaximumMarkdownUtf8BytesPerLocale
                    ? source.Markdown.Substring(0, LegalMarkdownContentLimits.MaximumMarkdownUtf8BytesPerLocale + 1)
                    : source.Markdown
            })
            .Take(maximumDocuments * LegalMarkdownContentLimits.MaximumLocalesPerDocument + 1)
            .ToListAsync(ct);
        if (sources.Count > maximumDocuments * LegalMarkdownContentLimits.MaximumLocalesPerDocument)
            throw new InvalidOperationException("configuration_import_legal_source_invalid");

        return documents.Select(document => new LegalDocumentPortableSource(
            document.Kind, document.Audience, document.RequiresFreshAcceptance,
            document.TemplateId is null && document.TemplateVersion is null
                && document.TemplateSourceKind is null && document.TemplateLicenseExpression is null
                && document.TemplateReviewReference is null
                ? null
                : LegalDocumentTemplateProvenance.Create(
                    document.TemplateId!, document.TemplateVersion!,
                    document.TemplateSourceKind ?? throw new InvalidOperationException("configuration_import_legal_source_invalid"),
                    document.TemplateLicenseExpression!, document.TemplateReviewReference!),
            sources.Where(source => source.LegalDocumentVersionId == document.VersionId)
                .OrderBy(source => source.LanguageTag, StringComparer.Ordinal)
                .Select(source => new LegalDocumentPortableLocalization(
                    source.LanguageTag, source.Title, source.Summary, source.Markdown))
                .ToImmutableArray())).ToArray();
    }
}
