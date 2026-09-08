namespace ISLAMU.Event.Setup.Core;

using System.Collections.ObjectModel;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public enum OfflineLegalDraftScope
{
    Instance,
    Tenant
}

public enum OfflineLegalDraftRole
{
    InstanceDraftAuthority,
    TenantDraftAuthority
}

public enum OfflineLegalAudience
{
    Public,
    RegisteredUsers,
    Administrators,
    Developers,
    EventParticipants
}

public enum OfflineLegalDocumentKind
{
    TermsOfService = 1,
    PrivacyNotice,
    CookiePolicy,
    AcceptableUsePolicy,
    CommunityGuidelines,
    ModerationReportingAppealPolicy,
    AccessibilityStatement,
    LegalNotice,
    SecurityDisclosurePolicy,
    RetentionErasurePortabilityNotice,
    SubprocessorNotice,
    OpenSourceAttribution,
    ApiDeveloperTerms,
    FederationNotice,
    PaymentResponsibilities,
    SupportAvailabilityEolMigrationNotice,
    TenantTerms,
    TenantPrivacyNotice,
    TenantCodeOfConduct,
    OrganizerSubmissionTerms,
    EventPublicationModerationPolicy,
    CancellationRefundPolicy,
    RegistrationParticipantPrivacyNotice,
    MediaPhotographyNotice,
    SafeguardingMinorParticipationPolicy,
    VenueAccessibilityPolicy,
    ComplaintCorrectionCopyrightNotice,
    SponsorshipPartnerDisclosure,
    TenantRetentionContactSharingNotice
}

public enum OfflineLegalDraftProvenanceKind
{
    Blank,
    ProjectOwnedReviewedTemplate,
    ApprovedFossReviewedTemplate,
    ImportedPortableDraft
}

public sealed record OfflineLegalDraftProvenance
{
    private OfflineLegalDraftProvenance(
        OfflineLegalDraftProvenanceKind kind, string? templateId, string? templateVersion,
        string? sourceKind, string? licenseExpression, string? reviewReference)
    {
        Kind = kind;
        TemplateId = templateId;
        TemplateVersion = templateVersion;
        SourceKind = sourceKind;
        LicenseExpression = licenseExpression;
        ReviewReference = reviewReference;
    }

    public OfflineLegalDraftProvenanceKind Kind { get; }
    public string? TemplateId { get; }
    public string? TemplateVersion { get; }
    public string? SourceKind { get; }
    public string? LicenseExpression { get; }
    public string? ReviewReference { get; }
    public override string ToString() =>
        $"{nameof(OfflineLegalDraftProvenance)}:Kind={Kind}:HasTemplate={TemplateId is not null}";

    public static OfflineLegalDraftProvenance Blank { get; } =
        new(OfflineLegalDraftProvenanceKind.Blank, null, null, null, null, null);

    public static OfflineLegalDraftProvenance ProjectOwned(
        string templateId, string templateVersion, string licenseExpression, string reviewReference) =>
        Reviewed(OfflineLegalDraftProvenanceKind.ProjectOwnedReviewedTemplate,
            templateId, templateVersion, "ProjectOwned", licenseExpression, reviewReference);

    public static OfflineLegalDraftProvenance ApprovedFoss(
        string templateId, string templateVersion, string licenseExpression, string reviewReference) =>
        Reviewed(OfflineLegalDraftProvenanceKind.ApprovedFossReviewedTemplate,
            templateId, templateVersion, "ApprovedFoss", licenseExpression, reviewReference);

    public static OfflineLegalDraftProvenance Imported(
        ConfigurationManifestLegalTemplateProvenanceV1Alpha2? provenance)
    {
        if (provenance is null)
            return new OfflineLegalDraftProvenance(
                OfflineLegalDraftProvenanceKind.ImportedPortableDraft, null, null, null, null, null);
        ValidateSourceKind(provenance.SourceKind);
        return new OfflineLegalDraftProvenance(
            OfflineLegalDraftProvenanceKind.ImportedPortableDraft,
            Required(provenance.TemplateId, 100, nameof(provenance)),
            Required(provenance.TemplateVersion, 50, nameof(provenance)),
            provenance.SourceKind,
            Required(provenance.LicenseExpression, 100, nameof(provenance)),
            Required(provenance.ReviewReference, 200, nameof(provenance)));
    }

    internal ConfigurationManifestLegalTemplateProvenanceV1Alpha2? ToWire()
    {
        if (TemplateId is null)
            return null;
        return new ConfigurationManifestLegalTemplateProvenanceV1Alpha2
        {
            TemplateId = TemplateId,
            TemplateVersion = TemplateVersion!,
            SourceKind = SourceKind!,
            LicenseExpression = LicenseExpression!,
            ReviewReference = ReviewReference!
        };
    }

    private static OfflineLegalDraftProvenance Reviewed(
        OfflineLegalDraftProvenanceKind kind, string templateId, string templateVersion,
        string sourceKind, string licenseExpression, string reviewReference) => new(
            kind, Required(templateId, 100, nameof(templateId)),
            Required(templateVersion, 50, nameof(templateVersion)), sourceKind,
            Required(licenseExpression, 100, nameof(licenseExpression)),
            Required(reviewReference, 200, nameof(reviewReference)));

    private static string Required(string value, int maximum, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        string normalized = value.Trim();
        if (normalized.Length > maximum)
            throw new ArgumentOutOfRangeException(parameter);
        return normalized;
    }

    private static void ValidateSourceKind(string sourceKind)
    {
        if (sourceKind is not ("ProjectOwned" or "ApprovedFoss"))
            throw new ArgumentException("Template source kind is not approved.", nameof(sourceKind));
    }
}

public sealed record OfflineLegalLocale
{
    private OfflineLegalLocale(string languageTag, string title, string summary, string markdown)
    {
        LanguageTag = languageTag;
        Title = title;
        Summary = summary;
        Markdown = markdown;
    }

    public string LanguageTag { get; }
    public string Title { get; }
    public string Summary { get; }
    public string Markdown { get; }

    public static OfflineLegalLocale Create(string languageTag, string title, string summary, string markdown)
    {
        string language = NormalizeLanguage(languageTag);
        string normalizedTitle = Required(title, LegalMarkdownContentLimits.MaximumTitleLength, nameof(title));
        string normalizedSummary = Required(summary, LegalMarkdownContentLimits.MaximumSummaryLength, nameof(summary));
        string normalizedMarkdown = LegalMarkdownCodec.Normalize(markdown);
        _ = LegalMarkdownCodec.Inspect(normalizedMarkdown);
        return new OfflineLegalLocale(language, normalizedTitle, normalizedSummary, normalizedMarkdown);
    }

    public override string ToString() =>
        $"{nameof(OfflineLegalLocale)}:Language={LanguageTag}:ContentPresent=True";

    internal ConfigurationManifestLegalDocumentLocalizedSourceV1Alpha2 ToWire() => new()
    {
        LanguageTag = LanguageTag,
        Title = Title,
        Summary = Summary,
        Markdown = Markdown
    };

    private static string NormalizeLanguage(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Trim().ToLowerInvariant();
        string[] subtags = normalized.Split('-');
        if (normalized.Length > LegalMarkdownContentLimits.MaximumLanguageTagLength
            || subtags.Any(subtag => subtag.Length == 0
                || subtag.Any(character => !char.IsAsciiLetterOrDigit(character))))
            throw new ArgumentException("Language tag is invalid.", nameof(value));
        return normalized;
    }

    private static string Required(string value, int maximum, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        string normalized = value.Trim();
        if (normalized.Length > maximum)
            throw new ArgumentOutOfRangeException(parameter);
        return normalized;
    }
}

public sealed class OfflineLegalPreview
{
    private readonly SetupDiagnostic[] _diagnostics;

    internal OfflineLegalPreview(bool isReady, string html, IEnumerable<SetupDiagnostic> diagnostics)
    {
        IsReady = isReady;
        Html = isReady ? html : string.Empty;
        _diagnostics = diagnostics.ToArray();
    }

    public bool IsReady { get; }
    public string Html { get; }
    public IReadOnlyList<SetupDiagnostic> Diagnostics =>
        Array.AsReadOnly((SetupDiagnostic[])_diagnostics.Clone());
    public override string ToString() =>
        $"{nameof(OfflineLegalPreview)}:Ready={IsReady}:Length={Html.Length}:Diagnostics={_diagnostics.Length}";
}

public sealed record OfflineLegalDraft
{
    private readonly string[] _jurisdictions;
    private readonly OfflineLegalLocale[] _localizations;

    private OfflineLegalDraft(
        OfflineLegalDraftScope scope, OfflineLegalDocumentKind kind, OfflineLegalAudience audience,
        bool requiresFreshAcceptance, IEnumerable<string> jurisdictions, string? changeSummary,
        OfflineLegalDraftProvenance provenance, IEnumerable<OfflineLegalLocale> localizations)
    {
        Scope = scope;
        Role = scope == OfflineLegalDraftScope.Instance
            ? OfflineLegalDraftRole.InstanceDraftAuthority : OfflineLegalDraftRole.TenantDraftAuthority;
        Kind = kind;
        DocumentKey = kind.ToString();
        Audience = audience;
        RequiresFreshAcceptance = requiresFreshAcceptance;
        _jurisdictions = SnapshotJurisdictions(jurisdictions);
        ChangeSummary = Optional(changeSummary, LegalMarkdownContentLimits.MaximumSummaryLength, nameof(changeSummary));
        Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
        _localizations = SnapshotLocales(localizations);
    }

    public OfflineLegalDraftScope Scope { get; }
    public OfflineLegalDraftRole Role { get; }
    public OfflineLegalDocumentKind Kind { get; }
    public string DocumentKey { get; }
    public OfflineLegalAudience Audience { get; }
    public bool RequiresFreshAcceptance { get; }
    public IReadOnlyList<string> JurisdictionAssumptions =>
        Array.AsReadOnly((string[])_jurisdictions.Clone());
    public string? ChangeSummary { get; }
    public OfflineLegalDraftProvenance Provenance { get; }
    public IReadOnlyList<OfflineLegalLocale> Localizations =>
        Array.AsReadOnly((OfflineLegalLocale[])_localizations.Clone());

    public static OfflineLegalDraft Create(
        OfflineLegalDraftScope scope, OfflineLegalDocumentKind kind, OfflineLegalAudience audience,
        bool requiresFreshAcceptance, IEnumerable<string> jurisdictionAssumptions, string? changeSummary,
        OfflineLegalDraftProvenance provenance, IEnumerable<OfflineLegalLocale> localizations)
    {
        ValidateKindScope(scope, kind);
        if (!Enum.IsDefined(audience))
            throw new ArgumentOutOfRangeException(nameof(audience));
        return new OfflineLegalDraft(scope, kind, audience, requiresFreshAcceptance,
            jurisdictionAssumptions, changeSummary, provenance, localizations);
    }

    public static OfflineLegalDraft FromWire(
        OfflineLegalDraftScope scope, string documentKey,
        ConfigurationManifestLegalDocumentV1Alpha2 source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!Enum.TryParse(source.Kind, false, out OfflineLegalDocumentKind kind)
            || !Enum.IsDefined(kind)
            || !string.Equals(documentKey, source.Kind, StringComparison.Ordinal)
            || !Enum.TryParse(source.Audience, false, out OfflineLegalAudience audience)
            || !Enum.IsDefined(audience)
            || source.LifecycleIntent is not ("Draft" or "ReviewRequired")
            || source.ProposedEffectiveAt is not null
            || source.AccountableIdentityReference is not null)
            throw new ArgumentException("Portable legal source carries invalid authority.", nameof(source));
        OfflineLegalDraftProvenance provenance = source.LifecycleIntent == "ReviewRequired"
            ? OfflineLegalDraftProvenance.Imported(source.TemplateProvenance)
            : source.TemplateProvenance is null ? OfflineLegalDraftProvenance.Blank
                : source.TemplateProvenance.SourceKind switch
                {
                    "ProjectOwned" => OfflineLegalDraftProvenance.ProjectOwned(
                        source.TemplateProvenance.TemplateId, source.TemplateProvenance.TemplateVersion,
                        source.TemplateProvenance.LicenseExpression, source.TemplateProvenance.ReviewReference),
                    "ApprovedFoss" => OfflineLegalDraftProvenance.ApprovedFoss(
                        source.TemplateProvenance.TemplateId, source.TemplateProvenance.TemplateVersion,
                        source.TemplateProvenance.LicenseExpression, source.TemplateProvenance.ReviewReference),
                    _ => throw new ArgumentException("Template source kind is not approved.", nameof(source))
                };
        if (source.Localizations is null || source.JurisdictionAssumptions is null
            || source.Localizations.Any(item => item is null))
            throw new ArgumentException("Portable legal source is incomplete.", nameof(source));
        OfflineLegalLocale[] locales = source.Localizations.Select(item => OfflineLegalLocale.Create(
            item.LanguageTag, item.Title, item.Summary, item.Markdown)).ToArray();
        return Create(scope, kind, audience, source.RequiresFreshAcceptance,
            source.JurisdictionAssumptions, source.ChangeSummary, provenance, locales);
    }

    public ConfigurationManifestLegalDocumentV1Alpha2 ToWire() => new()
    {
        Kind = Kind.ToString(),
        Audience = Audience.ToString(),
        LifecycleIntent = Provenance.Kind == OfflineLegalDraftProvenanceKind.ImportedPortableDraft
            ? "ReviewRequired" : "Draft",
        ProposedEffectiveAt = null,
        RequiresFreshAcceptance = RequiresFreshAcceptance,
        AccountableIdentityReference = null,
        ChangeSummary = ChangeSummary,
        TemplateProvenance = Provenance.ToWire(),
        JurisdictionAssumptions = _jurisdictions,
        Localizations = _localizations.Select(item => item.ToWire()).ToArray()
    };

    public OfflineLegalPreview Preview(
        string languageTag, IReadOnlyDictionary<string, string> identityPlaceholders)
    {
        ArgumentNullException.ThrowIfNull(identityPlaceholders);
        string language = languageTag.Trim().ToLowerInvariant();
        OfflineLegalLocale? locale = _localizations.FirstOrDefault(item =>
            string.Equals(item.LanguageTag, language, StringComparison.OrdinalIgnoreCase));
        if (locale is null)
            return new OfflineLegalPreview(false, string.Empty,
                [Diagnostic("legal-locale-not-found", "$.legal.localizations")]);
        LegalMarkdownRenderResult rendered = LegalMarkdownCodec.Render(locale.Markdown, identityPlaceholders);
        SetupDiagnostic[] diagnostics = rendered.Diagnostics.Select(item => Diagnostic(
            item.Code, "$.legal.preview", item.Code == LegalMarkdownDiagnosticCodes.IdentityUnresolved
                ? SetupDiagnosticSeverity.Error : SetupDiagnosticSeverity.Warning)).ToArray();
        return new OfflineLegalPreview(rendered.IsReady, rendered.Html, diagnostics);
    }

    public override string ToString() =>
        $"{nameof(OfflineLegalDraft)}:{Scope}:{Role}:{DocumentKey}:Locales={_localizations.Length}:Provenance={Provenance.Kind}";

    private static void ValidateKindScope(OfflineLegalDraftScope scope, OfflineLegalDocumentKind kind)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        bool tenantKind = kind is
            OfflineLegalDocumentKind.TenantTerms
            or OfflineLegalDocumentKind.TenantPrivacyNotice
            or OfflineLegalDocumentKind.TenantCodeOfConduct
            or OfflineLegalDocumentKind.OrganizerSubmissionTerms
            or OfflineLegalDocumentKind.EventPublicationModerationPolicy
            or OfflineLegalDocumentKind.CancellationRefundPolicy
            or OfflineLegalDocumentKind.RegistrationParticipantPrivacyNotice
            or OfflineLegalDocumentKind.MediaPhotographyNotice
            or OfflineLegalDocumentKind.SafeguardingMinorParticipationPolicy
            or OfflineLegalDocumentKind.VenueAccessibilityPolicy
            or OfflineLegalDocumentKind.ComplaintCorrectionCopyrightNotice
            or OfflineLegalDocumentKind.SponsorshipPartnerDisclosure
            or OfflineLegalDocumentKind.TenantRetentionContactSharingNotice;
        if (tenantKind != (scope == OfflineLegalDraftScope.Tenant))
            throw new ArgumentException("Legal document kind does not belong to the selected draft scope.", nameof(kind));
    }

    private static string[] SnapshotJurisdictions(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        string[] snapshot = values.Select(value => Required(value, 100, nameof(values)))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (snapshot.Length > 16)
            throw new ArgumentOutOfRangeException(nameof(values));
        return snapshot;
    }

    private static OfflineLegalLocale[] SnapshotLocales(IEnumerable<OfflineLegalLocale> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        OfflineLegalLocale?[] supplied = values.Cast<OfflineLegalLocale?>().ToArray();
        if (supplied.Any(item => item is null))
            throw new ArgumentException("Legal localizations are invalid.", nameof(values));
        OfflineLegalLocale[] snapshot = supplied.OfType<OfflineLegalLocale>()
            .OrderBy(item => item.LanguageTag, StringComparer.Ordinal).ToArray();
        if (snapshot.Length is < 1 or > LegalMarkdownContentLimits.MaximumLocalesPerDocument
            || snapshot.Select(item => item.LanguageTag).Distinct(StringComparer.OrdinalIgnoreCase).Count() != snapshot.Length)
            throw new ArgumentException("Legal localizations are invalid.", nameof(values));
        return snapshot;
    }

    private static string Required(string value, int maximum, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        string normalized = value.Trim();
        if (normalized.Length > maximum)
            throw new ArgumentOutOfRangeException(parameter);
        return normalized;
    }

    private static string? Optional(string? value, int maximum, string parameter)
    {
        if (value is null)
            return null;
        return Required(value, maximum, parameter);
    }

    private static SetupDiagnostic Diagnostic(
        string code, string path, SetupDiagnosticSeverity severity = SetupDiagnosticSeverity.Error) =>
        new(new SetupDiagnosticCode(code), new SetupDiagnosticPath(path), severity);
}
