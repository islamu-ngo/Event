namespace Explore.Application.Features.ConfigurationManifest.LegalDocuments;

using System.Collections.Immutable;
using Explore.Domain;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public static class LegalDocumentRenderDiagnosticCodes
{
    public const string NotPublished = "legal_document_not_published";
    public const string PublicationIntegrityInvalid =
        "legal_document_publication_integrity_invalid";
    public const string NotPublic = "legal_document_not_public";
    public const string LocaleFallback = "legal_document_locale_fallback";
    public const string TemplateReviewed = "legal_document_template_reviewed";
    public const string ImportedSourceReviewed =
        "legal_document_imported_source_reviewed";
}

public sealed class LegalDocumentRenderDiagnostic
{
    internal LegalDocumentRenderDiagnostic(string code, string? subject)
    {
        Code = code;
        Subject = subject;
    }

    public string Code { get; }
    public string? Subject { get; }
}

public sealed class LegalDocumentRenderView
{
    internal LegalDocumentRenderView(
        bool isReady,
        string html,
        string title,
        string summary,
        string languageTag,
        LegalDocumentScope? scope,
        LegalDocumentKind? kind,
        LegalDocumentOwnerRole? ownerRole,
        int? version,
        DateTime? effectiveAt,
        string? contentDigest,
        ImmutableArray<string> linkTargets,
        ImmutableArray<LegalDocumentRenderDiagnostic> diagnostics)
    {
        IsReady = isReady;
        Html = html;
        Title = title;
        Summary = summary;
        LanguageTag = languageTag;
        Scope = scope;
        Kind = kind;
        OwnerRole = ownerRole;
        Version = version;
        EffectiveAt = effectiveAt;
        ContentDigest = contentDigest;
        LinkTargets = linkTargets;
        Diagnostics = diagnostics;
    }

    public bool IsReady { get; }
    public string Html { get; }
    public string Title { get; }
    public string Summary { get; }
    public string LanguageTag { get; }
    public LegalDocumentScope? Scope { get; }
    public LegalDocumentKind? Kind { get; }
    public LegalDocumentOwnerRole? OwnerRole { get; }
    public int? Version { get; }
    public DateTime? EffectiveAt { get; }
    public string? ContentDigest { get; }
    public ImmutableArray<string> LinkTargets { get; }
    public ImmutableArray<LegalDocumentRenderDiagnostic> Diagnostics { get; }
}

public sealed class LegalDocumentRenderingService
{
    public LegalDocumentRenderView RenderPreview(
        LegalDocumentLocalizedSource source,
        string requestedLanguageTag,
        IReadOnlyDictionary<string, string> identityValues)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateRequestedLanguage(requestedLanguageTag);
        LegalMarkdownRenderResult rendered =
            ISLAMU.Wire.Contracts.ConfigurationPortability.LegalMarkdownCodec.Render(source.Markdown, identityValues);
        return CreateView(
            rendered,
            source,
            scope: null,
            kind: null,
            ownerRole: null,
            version: null,
            effectiveAt: null,
            contentDigest: null,
            additionalDiagnostics: []);
    }

    public LegalDocumentRenderView RenderLastPublished(
        LegalDocument document,
        string requestedLanguageTag,
        IReadOnlyDictionary<string, string> identityValues)
    {
        ArgumentNullException.ThrowIfNull(document);
        string requested = ValidateRequestedLanguage(requestedLanguageTag);
        LegalDocumentPublication? publication = document.Publications
            .OrderBy(item => item.OccurredAt)
            .ThenBy(item => item.LifecycleState)
            .ThenBy(item => item.Id)
            .LastOrDefault();
        if (publication is null
            || publication.LifecycleState != LegalDocumentLifecycleState.Published)
        {
            return NotReady(
                document,
                LegalDocumentRenderDiagnosticCodes.NotPublished);
        }

        LegalDocumentVersion? version = document.Versions.SingleOrDefault(
            item => item.Id == publication.LegalDocumentVersionId);
        if (version is null
            || version.State is not (
                LegalDocumentLifecycleState.Published
                or LegalDocumentLifecycleState.Retired)
            || !string.Equals(
                version.ContentDigest,
                publication.ContentDigest,
                StringComparison.Ordinal)
            || !string.Equals(
                version.AccountableIdentityReference,
                publication.AccountableIdentityReference,
                StringComparison.Ordinal)
            || version.PublishedAt is null)
        {
            return NotReady(
                document,
                LegalDocumentRenderDiagnosticCodes.PublicationIntegrityInvalid);
        }

        if (version.Audience != LegalDocumentAudience.Public)
        {
            return NotReady(
                document,
                LegalDocumentRenderDiagnosticCodes.NotPublic);
        }

        LegalDocumentLocalizedSource source =
            SelectLocale(version.Sources, requested);
        var diagnostics = ImmutableArray.CreateBuilder<LegalDocumentRenderDiagnostic>();
        if (!string.Equals(
                requested,
                source.LanguageTag,
                StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(new LegalDocumentRenderDiagnostic(
                LegalDocumentRenderDiagnosticCodes.LocaleFallback,
                source.LanguageTag));
        }

        if (version.TemplateId is not null)
        {
            diagnostics.Add(new LegalDocumentRenderDiagnostic(
                LegalDocumentRenderDiagnosticCodes.TemplateReviewed,
                subject: null));
        }

        if (version.IsImported)
        {
            diagnostics.Add(new LegalDocumentRenderDiagnostic(
                LegalDocumentRenderDiagnosticCodes.ImportedSourceReviewed,
                subject: null));
        }

        LegalMarkdownRenderResult rendered =
            ISLAMU.Wire.Contracts.ConfigurationPortability.LegalMarkdownCodec.Render(source.Markdown, identityValues);
        return CreateView(
            rendered,
            source,
            document.Scope,
            document.Kind,
            document.OwnerRole,
            publication.Version,
            publication.EffectiveAt,
            publication.ContentDigest,
            diagnostics.ToImmutable());
    }

    private static LegalDocumentRenderView CreateView(
        LegalMarkdownRenderResult rendered,
        LegalDocumentLocalizedSource source,
        LegalDocumentScope? scope,
        LegalDocumentKind? kind,
        LegalDocumentOwnerRole? ownerRole,
        int? version,
        DateTime? effectiveAt,
        string? contentDigest,
        ImmutableArray<LegalDocumentRenderDiagnostic> additionalDiagnostics)
    {
        ImmutableArray<LegalDocumentRenderDiagnostic> diagnostics =
        [
            .. additionalDiagnostics,
            .. rendered.Diagnostics.Select(item =>
                new LegalDocumentRenderDiagnostic(item.Code, item.Subject))
        ];
        return new LegalDocumentRenderView(
            rendered.IsReady,
            rendered.Html,
            source.Title,
            source.Summary,
            source.LanguageTag,
            scope,
            kind,
            ownerRole,
            version,
            effectiveAt,
            contentDigest,
            rendered.LinkTargets,
            diagnostics);
    }

    private static LegalDocumentRenderView NotReady(
        LegalDocument document,
        string code) =>
        new(
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            document.Scope,
            document.Kind,
            document.OwnerRole,
            version: null,
            effectiveAt: null,
            contentDigest: null,
            [],
            [new LegalDocumentRenderDiagnostic(code, subject: null)]);

    private static LegalDocumentLocalizedSource SelectLocale(
        IReadOnlyList<LegalDocumentLocalizedSource> sources,
        string requestedLanguageTag)
    {
        LegalDocumentLocalizedSource? exact = sources.FirstOrDefault(source =>
            string.Equals(
                source.LanguageTag,
                requestedLanguageTag,
                StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        int separator = requestedLanguageTag.IndexOf('-');
        if (separator > 0)
        {
            string primary = requestedLanguageTag[..separator];
            LegalDocumentLocalizedSource? baseLanguage = sources.FirstOrDefault(
                source => string.Equals(
                    source.LanguageTag,
                    primary,
                    StringComparison.OrdinalIgnoreCase));
            if (baseLanguage is not null)
                return baseLanguage;
        }

        return sources.FirstOrDefault(source => string.Equals(
                source.LanguageTag,
                "en",
                StringComparison.OrdinalIgnoreCase))
            ?? sources.OrderBy(
                    source => source.LanguageTag,
                    StringComparer.Ordinal)
                .First();
    }

    private static string ValidateRequestedLanguage(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Trim();
        if (normalized.Length > ISLAMU.Wire.Contracts.ConfigurationPortability.LegalMarkdownContentLimits.MaximumLanguageTagLength)
            throw new ArgumentOutOfRangeException(nameof(value));
        return normalized;
    }
}
