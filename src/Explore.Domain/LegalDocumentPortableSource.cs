namespace Explore.Domain;

using System.Collections.Immutable;
using System.Text;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

/// <summary>
/// Current legal source values, without aggregate identity, review authority, or publication history.
/// </summary>
public sealed record LegalDocumentPortableSource
{
    public LegalDocumentPortableSource(
        LegalDocumentKind kind,
        LegalDocumentAudience audience,
        bool requiresFreshAcceptance,
        LegalDocumentTemplateProvenance? templateProvenance,
        ImmutableArray<LegalDocumentPortableLocalization> localizations)
    {
        _ = LegalDocumentKindCatalog.Get(kind);
        if (!Enum.IsDefined(audience))
            throw new ArgumentOutOfRangeException(nameof(audience));
        if (localizations.IsDefaultOrEmpty
            || localizations.Length > LegalMarkdownContentLimits.MaximumLocalesPerDocument
            || localizations.Any(localization => localization is null)
            || localizations.Select(localization => localization.LanguageTag)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != localizations.Length)
            throw new ArgumentException("Legal source locales must be bounded, nonempty, and unique.", nameof(localizations));

        Kind = kind;
        Audience = audience;
        RequiresFreshAcceptance = requiresFreshAcceptance;
        TemplateProvenance = templateProvenance;
        Localizations = localizations;
    }

    public LegalDocumentKind Kind { get; }
    public LegalDocumentAudience Audience { get; }
    public bool RequiresFreshAcceptance { get; }
    public LegalDocumentTemplateProvenance? TemplateProvenance { get; }
    public ImmutableArray<LegalDocumentPortableLocalization> Localizations { get; }
}

public sealed record LegalDocumentPortableLocalization
{
    public LegalDocumentPortableLocalization(
        string languageTag, string title, string summary, string markdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentNullException.ThrowIfNull(markdown);
        if (languageTag.Length > LegalMarkdownContentLimits.MaximumLanguageTagLength
            || title.Length > LegalMarkdownContentLimits.MaximumTitleLength
            || summary.Length > LegalMarkdownContentLimits.MaximumSummaryLength
            || markdown.Contains('\r', StringComparison.Ordinal)
            || Encoding.UTF8.GetByteCount(markdown) is < 1
                or > LegalMarkdownContentLimits.MaximumMarkdownUtf8BytesPerLocale)
            throw new ArgumentException("Legal source content exceeds its authoritative bounds.");

        LegalMarkdownInspection inspection = LegalMarkdownCodec.Inspect(markdown);
        if (inspection.LinkCount > LegalMarkdownContentLimits.MaximumLinksPerLocale
            || inspection.PlaceholderCount > LegalMarkdownContentLimits.MaximumPlaceholdersPerLocale)
            throw new ArgumentException("Legal source Markdown exceeds its authoritative bounds.", nameof(markdown));

        LanguageTag = languageTag;
        Title = title;
        Summary = summary;
        Markdown = markdown;
    }

    public string LanguageTag { get; }
    public string Title { get; }
    public string Summary { get; }
    public string Markdown { get; }
}
