namespace Explore.Application.Features.ConfigurationManifest.Application;

using System.Collections.ObjectModel;
using Explore.Domain;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

/// <summary>
/// Projects current native authoring content, never publication permission, into portable Draft intent.
/// Native storage has no jurisdiction-assumption or change-summary fields. Their portable forms
/// are therefore empty and null, respectively; callers must normalize unsupported incoming metadata
/// before comparing fidelity, or reject it. Accountable identity and effective time are target-local
/// authority and are deliberately omitted rather than copied as approval or scheduling permission.
/// </summary>
public static class LegalConfigurationSnapshotProjection
{
    public static ConfigurationManifestLegalDocumentV1Alpha2 NormalizeDraft(
        ConfigurationManifestLegalDocumentV1Alpha2 source)
    {
        var localizations = source.Localizations.Select(row => LegalDocumentLocalizedSource.Create(
            row.LanguageTag, row.Title, row.Summary, row.Markdown))
            .OrderBy(row => row.LanguageTag, StringComparer.Ordinal)
            .Select(row => new ConfigurationManifestLegalDocumentLocalizedSourceV1Alpha2
            {
                LanguageTag = row.LanguageTag, Title = row.Title, Summary = row.Summary, Markdown = row.Markdown
            }).ToArray();
        ConfigurationManifestLegalTemplateProvenanceV1Alpha2? template = null;
        if (source.TemplateProvenance is { } portable)
        {
            var native = LegalDocumentTemplateProvenance.Create(
                portable.TemplateId, portable.TemplateVersion,
                Enum.Parse<LegalDocumentTemplateSourceKind>(portable.SourceKind, ignoreCase: false),
                portable.LicenseExpression, portable.ReviewReference);
            template = new()
            {
                TemplateId = native.TemplateId, TemplateVersion = native.TemplateVersion,
                SourceKind = native.SourceKind.ToString(), LicenseExpression = native.LicenseExpression,
                ReviewReference = native.ReviewReference
            };
        }
        return source with
        {
            LifecycleIntent = "Draft", AccountableIdentityReference = null, ProposedEffectiveAt = null,
            Localizations = localizations, TemplateProvenance = template
        };
    }

    public static IReadOnlyDictionary<string, ConfigurationManifestLegalDocumentV1Alpha2> Project(
        IReadOnlyList<LegalDocument> documents,
        LegalDocumentScope scope,
        Guid? tenantId)
    {
        ArgumentNullException.ThrowIfNull(documents);
        string authorityKey = scope switch
        {
            LegalDocumentScope.Instance when tenantId is null => "instance",
            LegalDocumentScope.Tenant when tenantId is { } id && id != Guid.Empty => $"tenant:{id:N}",
            _ => throw new ArgumentException("Legal snapshot scope and tenant are inconsistent.", nameof(tenantId))
        };
        var result = new SortedDictionary<string, ConfigurationManifestLegalDocumentV1Alpha2>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            if (document is null || document.Scope != scope || document.TenantId != tenantId
                || document.AuthorityKey != authorityKey
                || LegalDocumentKindCatalog.Get(document.Kind).Scope != scope)
                throw new InvalidOperationException("Legal snapshot must contain only the exact requested scope.");
            var version = document.Versions.Single(row => row.Version == document.CurrentVersion);
            if (version.LegalDocumentId != document.Id || !Enum.IsDefined(version.Audience)
                || version.Sources.Count == 0
                || version.Sources.Any(row => row.LegalDocumentVersionId != version.Id))
                throw new InvalidOperationException("Legal snapshot requires the complete current authoring graph.");
            ConfigurationManifestLegalTemplateProvenanceV1Alpha2? template = null;
            if (version.TemplateId is not null || version.TemplateVersion is not null
                || version.TemplateSourceKind is not null || version.TemplateLicenseExpression is not null
                || version.TemplateReviewReference is not null)
            {
                if (version.TemplateId is null || version.TemplateVersion is null
                    || version.TemplateSourceKind is not { } sourceKind || !Enum.IsDefined(sourceKind)
                    || version.TemplateLicenseExpression is null || version.TemplateReviewReference is null)
                    throw new InvalidOperationException("Legal snapshot template provenance must be complete.");
                template = new()
                {
                    TemplateId = version.TemplateId,
                    TemplateVersion = version.TemplateVersion,
                    SourceKind = sourceKind.ToString(),
                    LicenseExpression = version.TemplateLicenseExpression,
                    ReviewReference = version.TemplateReviewReference
                };
            }
            // Validator keys are enum names, not the slug codes used by public legal routes.
            string kind = document.Kind.ToString();
            if (!result.TryAdd(kind, new()
            {
                Kind = kind,
                Audience = version.Audience.ToString(),
                LifecycleIntent = "Draft",
                RequiresFreshAcceptance = version.RequiresFreshAcceptance,
                AccountableIdentityReference = null,
                ChangeSummary = null,
                ProposedEffectiveAt = null,
                JurisdictionAssumptions = [],
                TemplateProvenance = template,
                Localizations = version.Sources.OrderBy(row => row.LanguageTag, StringComparer.Ordinal)
                    .Select(row => new ConfigurationManifestLegalDocumentLocalizedSourceV1Alpha2
                    {
                        LanguageTag = row.LanguageTag,
                        Title = row.Title,
                        Summary = row.Summary,
                        Markdown = row.Markdown
                    }).ToArray()
            }))
                throw new InvalidOperationException("Legal snapshot kinds must be unique within their scope.");
        }
        return new ReadOnlyDictionary<string, ConfigurationManifestLegalDocumentV1Alpha2>(result);
    }
}
