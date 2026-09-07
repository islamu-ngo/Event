namespace Explore.Domain;

using System.Collections.Frozen;

public enum LegalDocumentScope
{
    Instance = 1,
    Tenant = 2
}

public enum LegalDocumentOwnerRole
{
    InstanceOperator = 1,
    TenantOperator = 2
}

public enum LegalDocumentAudience
{
    Public = 1,
    RegisteredUsers = 2,
    Administrators = 3,
    Developers = 4,
    EventParticipants = 5
}

public enum LegalDocumentLifecycleState
{
    Draft = 1,
    ReviewRequired = 2,
    Approved = 3,
    Scheduled = 4,
    Published = 5,
    Retired = 6
}

public enum LegalDocumentTemplateSourceKind
{
    ProjectOwned = 1,
    ApprovedFoss = 2
}

public enum LegalDocumentKind
{
    TermsOfService = 1,
    PrivacyNotice = 2,
    CookiePolicy = 3,
    AcceptableUsePolicy = 4,
    CommunityGuidelines = 5,
    ModerationReportingAppealPolicy = 6,
    AccessibilityStatement = 7,
    LegalNotice = 8,
    SecurityDisclosurePolicy = 9,
    RetentionErasurePortabilityNotice = 10,
    SubprocessorNotice = 11,
    OpenSourceAttribution = 12,
    ApiDeveloperTerms = 13,
    FederationNotice = 14,
    PaymentResponsibilities = 15,
    SupportAvailabilityEolMigrationNotice = 16,
    TenantTerms = 17,
    TenantPrivacyNotice = 18,
    TenantCodeOfConduct = 19,
    OrganizerSubmissionTerms = 20,
    EventPublicationModerationPolicy = 21,
    CancellationRefundPolicy = 22,
    RegistrationParticipantPrivacyNotice = 23,
    MediaPhotographyNotice = 24,
    SafeguardingMinorParticipationPolicy = 25,
    VenueAccessibilityPolicy = 26,
    ComplaintCorrectionCopyrightNotice = 27,
    SponsorshipPartnerDisclosure = 28,
    TenantRetentionContactSharingNotice = 29
}

public sealed record LegalDocumentKindDescriptor(
    LegalDocumentKind Kind,
    LegalDocumentScope Scope,
    LegalDocumentOwnerRole OwnerRole)
{
    public string Code => LegalDocumentKindCatalog.CodeFor(Kind);
}

public static class LegalDocumentKindCatalog
{
    private static readonly FrozenDictionary<LegalDocumentKind, LegalDocumentKindDescriptor>
        KindEntries = Create();
    private static readonly FrozenDictionary<string, LegalDocumentKindDescriptor>
        CodeEntries = KindEntries.Values.ToFrozenDictionary(
            descriptor => descriptor.Code,
            StringComparer.Ordinal);

    public static IReadOnlyDictionary<LegalDocumentKind, LegalDocumentKindDescriptor>
        Entries => KindEntries;

    public static LegalDocumentKindDescriptor Get(LegalDocumentKind kind) =>
        KindEntries.TryGetValue(kind, out LegalDocumentKindDescriptor? descriptor)
            ? descriptor
            : throw new ArgumentOutOfRangeException(nameof(kind));

    public static bool TryGet(
        string code,
        out LegalDocumentKindDescriptor? descriptor)
    {
        descriptor = null;
        return !string.IsNullOrWhiteSpace(code)
            && CodeEntries.TryGetValue(code, out descriptor);
    }

    public static string CodeFor(LegalDocumentKind kind) => kind switch
    {
        LegalDocumentKind.TermsOfService => "terms-of-service",
        LegalDocumentKind.PrivacyNotice => "privacy-notice",
        LegalDocumentKind.CookiePolicy => "cookie-policy",
        LegalDocumentKind.AcceptableUsePolicy => "acceptable-use-policy",
        LegalDocumentKind.CommunityGuidelines => "community-guidelines",
        LegalDocumentKind.ModerationReportingAppealPolicy =>
            "moderation-reporting-appeal-policy",
        LegalDocumentKind.AccessibilityStatement => "accessibility-statement",
        LegalDocumentKind.LegalNotice => "legal-notice",
        LegalDocumentKind.SecurityDisclosurePolicy => "security-disclosure-policy",
        LegalDocumentKind.RetentionErasurePortabilityNotice =>
            "retention-erasure-portability-notice",
        LegalDocumentKind.SubprocessorNotice => "subprocessor-notice",
        LegalDocumentKind.OpenSourceAttribution => "open-source-attribution",
        LegalDocumentKind.ApiDeveloperTerms => "api-developer-terms",
        LegalDocumentKind.FederationNotice => "federation-notice",
        LegalDocumentKind.PaymentResponsibilities => "payment-responsibilities",
        LegalDocumentKind.SupportAvailabilityEolMigrationNotice =>
            "support-availability-eol-migration-notice",
        LegalDocumentKind.TenantTerms => "tenant-terms",
        LegalDocumentKind.TenantPrivacyNotice => "tenant-privacy-notice",
        LegalDocumentKind.TenantCodeOfConduct => "tenant-code-of-conduct",
        LegalDocumentKind.OrganizerSubmissionTerms => "organizer-submission-terms",
        LegalDocumentKind.EventPublicationModerationPolicy =>
            "event-publication-moderation-policy",
        LegalDocumentKind.CancellationRefundPolicy => "cancellation-refund-policy",
        LegalDocumentKind.RegistrationParticipantPrivacyNotice =>
            "registration-participant-privacy-notice",
        LegalDocumentKind.MediaPhotographyNotice => "media-photography-notice",
        LegalDocumentKind.SafeguardingMinorParticipationPolicy =>
            "safeguarding-minor-participation-policy",
        LegalDocumentKind.VenueAccessibilityPolicy =>
            "venue-accessibility-policy",
        LegalDocumentKind.ComplaintCorrectionCopyrightNotice =>
            "complaint-correction-copyright-notice",
        LegalDocumentKind.SponsorshipPartnerDisclosure =>
            "sponsorship-partner-disclosure",
        LegalDocumentKind.TenantRetentionContactSharingNotice =>
            "tenant-retention-contact-sharing-notice",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static FrozenDictionary<LegalDocumentKind, LegalDocumentKindDescriptor>
        Create() =>
        Enum.GetValues<LegalDocumentKind>()
            .Select(kind =>
            {
                LegalDocumentScope scope = kind <=
                    LegalDocumentKind.SupportAvailabilityEolMigrationNotice
                    ? LegalDocumentScope.Instance
                    : LegalDocumentScope.Tenant;
                LegalDocumentOwnerRole role = scope == LegalDocumentScope.Instance
                    ? LegalDocumentOwnerRole.InstanceOperator
                    : LegalDocumentOwnerRole.TenantOperator;
                return new LegalDocumentKindDescriptor(kind, scope, role);
            })
            .ToFrozenDictionary(descriptor => descriptor.Kind);
}
