namespace Explore.Domain;

using System.Text;

public sealed record LegalDocumentTemplateProvenance
{
    private LegalDocumentTemplateProvenance(
        string templateId,
        string templateVersion,
        LegalDocumentTemplateSourceKind sourceKind,
        string licenseExpression,
        string reviewReference)
    {
        TemplateId = templateId;
        TemplateVersion = templateVersion;
        SourceKind = sourceKind;
        LicenseExpression = licenseExpression;
        ReviewReference = reviewReference;
    }

    public string TemplateId { get; }
    public string TemplateVersion { get; }
    public LegalDocumentTemplateSourceKind SourceKind { get; }
    public string LicenseExpression { get; }
    public string ReviewReference { get; }
    public bool IsLegalAdvice => false;
    public bool IsCertification => false;

    public static LegalDocumentTemplateProvenance Create(
        string templateId,
        string templateVersion,
        LegalDocumentTemplateSourceKind sourceKind,
        string licenseExpression,
        string reviewReference)
    {
        if (!Enum.IsDefined(sourceKind))
            throw new ArgumentOutOfRangeException(nameof(sourceKind));

        return new LegalDocumentTemplateProvenance(
            NormalizeRequired(templateId, 100, nameof(templateId)),
            NormalizeRequired(templateVersion, 50, nameof(templateVersion)),
            sourceKind,
            NormalizeRequired(licenseExpression, 100, nameof(licenseExpression)),
            NormalizeRequired(reviewReference, 200, nameof(reviewReference)));
    }

    private static string NormalizeRequired(
        string value,
        int maximumLength,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new ArgumentOutOfRangeException(parameterName);
        return normalized;
    }
}

public sealed class LegalDocumentLocalizedSource
{
    private LegalDocumentLocalizedSource()
    {
    }

    public Guid Id { get; private set; }
    public Guid LegalDocumentVersionId { get; private set; }
    public LegalDocumentVersion? LegalDocumentVersion { get; private set; }
    public string LanguageTag { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Summary { get; private set; } = string.Empty;
    public string Markdown { get; private set; } = string.Empty;
    public int Utf8ByteCount { get; private set; }
    public int LinkCount { get; private set; }
    public int PlaceholderCount { get; private set; }

    public static LegalDocumentLocalizedSource Create(
        string languageTag,
        string title,
        string summary,
        string markdown)
    {
        string normalizedLanguage = NormalizeLanguageTag(languageTag);
        string normalizedTitle = NormalizeText(
            title,
            ISLAMU.Wire.Contracts.ConfigurationPortability.LegalMarkdownContentLimits.MaximumTitleLength,
            nameof(title));
        string normalizedSummary = NormalizeText(
            summary,
            ISLAMU.Wire.Contracts.ConfigurationPortability.LegalMarkdownContentLimits.MaximumSummaryLength,
            nameof(summary));
        ArgumentNullException.ThrowIfNull(markdown);
        string normalizedMarkdown = markdown.Replace(
            "\r\n",
            "\n",
            StringComparison.Ordinal);
        if (normalizedMarkdown.Contains('\r', StringComparison.Ordinal))
            throw new ArgumentException("Legal Markdown must use canonical line endings.", nameof(markdown));

        int byteCount = Encoding.UTF8.GetByteCount(normalizedMarkdown);
        if (byteCount is < 1 or >
            ISLAMU.Wire.Contracts.ConfigurationPortability.LegalMarkdownContentLimits.MaximumMarkdownUtf8BytesPerLocale)
        {
            throw new ArgumentOutOfRangeException(nameof(markdown));
        }

        ISLAMU.Wire.Contracts.ConfigurationPortability.LegalMarkdownInspection shape =
            ISLAMU.Wire.Contracts.ConfigurationPortability.LegalMarkdownCodec.Inspect(normalizedMarkdown);
        return new LegalDocumentLocalizedSource
        {
            Id = Guid.CreateVersion7(),
            LanguageTag = normalizedLanguage,
            Title = normalizedTitle,
            Summary = normalizedSummary,
            Markdown = normalizedMarkdown,
            Utf8ByteCount = byteCount,
            LinkCount = shape.LinkCount,
            PlaceholderCount = shape.PlaceholderCount
        };
    }

    internal void BindVersion(Guid versionId, LegalDocumentVersion version)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(versionId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(version);
        if (LegalDocumentVersionId != Guid.Empty)
            throw new InvalidOperationException("Localized legal source is already bound.");

        LegalDocumentVersionId = versionId;
        LegalDocumentVersion = version;
    }

    private static string NormalizeLanguageTag(string languageTag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);
        string normalized = languageTag.Trim();
        if (normalized.Length > ISLAMU.Wire.Contracts.ConfigurationPortability.LegalMarkdownContentLimits.MaximumLanguageTagLength)
            throw new ArgumentOutOfRangeException(nameof(languageTag));

        string[] segments = normalized.Split(
            '-',
            StringSplitOptions.RemoveEmptyEntries);
        bool privateUse = segments.Length >= 2
            && string.Equals(segments[0], "x", StringComparison.OrdinalIgnoreCase);
        bool validPrimary = privateUse
            || segments.Length > 0
            && segments[0].Length is 2 or 3
            && segments[0].All(char.IsAsciiLetter);
        if (!validPrimary
            || segments.Skip(1).Any(segment =>
                segment.Length is < 1 or > 8
                || !segment.All(char.IsAsciiLetterOrDigit)))
        {
            throw new ArgumentException("Language tag is invalid.", nameof(languageTag));
        }

        return string.Join(
            '-',
            segments.Select((segment, index) =>
                index == 0 && !privateUse
                    ? segment.ToLowerInvariant()
                    : segment.Length == 2 && segment.All(char.IsAsciiLetter)
                        ? segment.ToUpperInvariant()
                        : segment.ToLowerInvariant()));
    }

    private static string NormalizeText(
        string value,
        int maximumLength,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new ArgumentOutOfRangeException(parameterName);
        return normalized;
    }
}
