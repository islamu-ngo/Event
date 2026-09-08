namespace Event.Domain.UnitTests.ConfigurationManifest;

using System.Collections;
using System.Reflection;
using Explore.Domain;

public sealed class LegalDocumentPortabilityInvariantTests
{
    private static readonly Assembly DomainAssembly =
        typeof(ConfigurationManifestOperation).Assembly;

    private static readonly DateTime OccurredAt =
        new(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);

    private static readonly string[] InstanceKinds =
    [
        "TermsOfService",
        "PrivacyNotice",
        "CookiePolicy",
        "AcceptableUsePolicy",
        "CommunityGuidelines",
        "ModerationReportingAppealPolicy",
        "AccessibilityStatement",
        "LegalNotice",
        "SecurityDisclosurePolicy",
        "RetentionErasurePortabilityNotice",
        "SubprocessorNotice",
        "OpenSourceAttribution",
        "ApiDeveloperTerms",
        "FederationNotice",
        "PaymentResponsibilities",
        "SupportAvailabilityEolMigrationNotice"
    ];

    private static readonly string[] TenantKinds =
    [
        "TenantTerms",
        "TenantPrivacyNotice",
        "TenantCodeOfConduct",
        "OrganizerSubmissionTerms",
        "EventPublicationModerationPolicy",
        "CancellationRefundPolicy",
        "RegistrationParticipantPrivacyNotice",
        "MediaPhotographyNotice",
        "SafeguardingMinorParticipationPolicy",
        "VenueAccessibilityPolicy",
        "ComplaintCorrectionCopyrightNotice",
        "SponsorshipPartnerDisclosure",
        "TenantRetentionContactSharingNotice"
    ];

    [Test]
    public async Task KindCatalog_IsClosedAndAssignsOneOwnerRolePerScope()
    {
        Type kindType = RequireType("LegalDocumentKind");
        Type catalogType = RequireType("LegalDocumentKindCatalog");
        IReadOnlyDictionary<string, object> entries =
            ReadStaticDictionary(catalogType, "Entries");

        await Assert.That(Enum.GetNames(kindType))
            .IsEquivalentTo(InstanceKinds.Concat(TenantKinds).ToArray());
        await Assert.That(entries.Keys)
            .IsEquivalentTo(InstanceKinds.Concat(TenantKinds).ToArray());

        foreach ((string kind, object descriptor) in entries)
        {
            bool instanceOwned = InstanceKinds.Contains(kind, StringComparer.Ordinal);
            await Assert.That(ReadValue(descriptor, "Kind").ToString())
                .IsEqualTo(kind);
            await Assert.That(ReadValue(descriptor, "Scope").ToString())
                .IsEqualTo(instanceOwned ? "Instance" : "Tenant");
            await Assert.That(ReadValue(descriptor, "OwnerRole").ToString())
                .IsEqualTo(instanceOwned ? "InstanceOperator" : "TenantOperator");
        }
    }

    [Test]
    public async Task CreateDraft_EnforcesScopeAndKeepsAcceptanceFactsSeparate()
    {
        object source = CreateSource("# Policy\n\nPortable source.");
        object provenance = CreateProvenance("ProjectOwned", "ISLAMU-Internal");
        object aggregate = CreateDraft(
            scope: "Tenant",
            tenantId: Guid.CreateVersion7(),
            kind: "TenantTerms",
            source,
            provenance,
            accountableIdentityReference: "tenant-directory-identity:v1");

        await Assert.That(((Guid)ReadValue(aggregate, "Id")).Version).IsEqualTo(7);
        await Assert.That(ReadValue(aggregate, "State").ToString()).IsEqualTo("Draft");
        await Assert.That(ReadValue(aggregate, "OwnerRole").ToString())
            .IsEqualTo("TenantOperator");
        await Assert.That(ReadEnumerable(aggregate, "Versions").Cast<object>().Count())
            .IsEqualTo(1);
        await Assert.That(ReadEnumerable(aggregate, "Publications").Cast<object>())
            .IsEmpty();

        string[] forbiddenEvidenceProperties =
        [
            "AcceptedAt",
            "AcceptedBy",
            "AcceptanceHistory",
            "AcceptanceRecordId",
            "UserId",
            "SubjectId"
        ];
        foreach (Type type in new[]
                 {
                     aggregate.GetType(),
                     RequireType("LegalDocumentVersion"),
                     RequireType("LegalDocumentPublication")
                 })
        {
            await Assert.That(type.GetProperties()
                    .Select(property => property.Name)
                    .Intersect(forbiddenEvidenceProperties, StringComparer.Ordinal))
                .IsEmpty();
        }

        await Assert.That(() => CreateDraft(
                scope: "Instance",
                tenantId: Guid.CreateVersion7(),
                kind: "TermsOfService",
                source,
                provenance,
                accountableIdentityReference: "instance-identity:v1"))
            .Throws<TargetInvocationException>();
        await Assert.That(() => CreateDraft(
                scope: "Tenant",
                tenantId: Guid.CreateVersion7(),
                kind: "TermsOfService",
                source,
                provenance,
                accountableIdentityReference: "tenant-identity:v1"))
            .Throws<TargetInvocationException>();
    }

    [Test]
    public async Task Lifecycle_PublishesAppendOnlyVersionEvidenceThenRetires()
    {
        object aggregate = CreateDraft(
            "Instance",
            tenantId: null,
            "TermsOfService",
            CreateSource("# Policy\n\nVersion one."),
            CreateProvenance("ProjectOwned", "ISLAMU-Internal"),
            "instance-identity:v1");

        Invoke(aggregate, "SubmitForReview", OccurredAt.AddMinutes(1));
        await Assert.That(ReadValue(aggregate, "State").ToString())
            .IsEqualTo("ReviewRequired");
        Invoke(
            aggregate,
            "Approve",
            Guid.CreateVersion7(),
            "review-evidence:2026-08",
            OccurredAt.AddMinutes(2));
        await Assert.That(ReadValue(aggregate, "State").ToString())
            .IsEqualTo("Approved");
        Invoke(
            aggregate,
            "Schedule",
            OccurredAt.AddMinutes(4),
            OccurredAt.AddMinutes(3));
        await Assert.That(ReadValue(aggregate, "State").ToString())
            .IsEqualTo("Scheduled");

        object publication = Invoke(
            aggregate,
            "Publish",
            OccurredAt.AddMinutes(4))!;
        string digest = ReadValue(publication, "ContentDigest").ToString()!;
        await Assert.That(ReadValue(aggregate, "State").ToString())
            .IsEqualTo("Published");
        await Assert.That(ReadEnumerable(aggregate, "Publications").Cast<object>().Count())
            .IsEqualTo(1);

        object retirement = Invoke(
            aggregate,
            "Retire",
            OccurredAt.AddMinutes(5))!;
        object firstEvidence = ReadEnumerable(aggregate, "Publications")
            .Cast<object>()
            .First();
        await Assert.That(ReadValue(aggregate, "State").ToString())
            .IsEqualTo("Retired");
        await Assert.That(ReadEnumerable(aggregate, "Publications").Cast<object>().Count())
            .IsEqualTo(2);
        await Assert.That(ReadValue(firstEvidence, "ContentDigest").ToString())
            .IsEqualTo(digest);
        await Assert.That(ReadValue(retirement, "LifecycleState").ToString())
            .IsEqualTo("Retired");
    }

    [Test]
    public async Task ImportedSource_RemainsTargetReviewRequiredAndCannotPublishDirectly()
    {
        Type aggregateType = RequireType("LegalDocument");
        object source = CreateSource(
            "# Policy\n\nAccountable party: {{accountable_identity}}.");
        object provenance = CreateProvenance(
            "ApprovedFoss",
            "MIT");
        MethodInfo import = RequireMethod(aggregateType, "CreateImportedDraft");
        Guid targetTenantId = Guid.CreateVersion7();
        object aggregate = import.Invoke(
            null,
            [
                Enum.Parse(RequireType("LegalDocumentScope"), "Tenant"),
                targetTenantId,
                Enum.Parse(RequireType("LegalDocumentKind"), "TenantPrivacyNotice"),
                Enum.Parse(RequireType("LegalDocumentAudience"), "Public"),
                TypedArray(source),
                provenance,
                "source-origin:manifest-digest",
                true,
                OccurredAt
            ])!;

        await Assert.That(ReadValue(aggregate, "State").ToString())
            .IsEqualTo("ReviewRequired");
        await Assert.That(ReadValue(aggregate, "TenantId")).IsEqualTo(targetTenantId);
        await Assert.That(ReadValue(aggregate, "AccountableIdentityReference")).IsNull();
        await Assert.That(() => Invoke(
                aggregate,
                "Publish",
                OccurredAt.AddMinutes(1)))
            .Throws<TargetInvocationException>();

        Invoke(
            aggregate,
            "BindAccountableIdentity",
            "target-tenant-identity:v2",
            OccurredAt.AddMinutes(1));
        Invoke(
            aggregate,
            "Approve",
            Guid.CreateVersion7(),
            "target-review:evidence",
            OccurredAt.AddMinutes(2));
        Invoke(
            aggregate,
            "Schedule",
            OccurredAt.AddMinutes(4),
            OccurredAt.AddMinutes(3));
        object publication = Invoke(
            aggregate,
            "Publish",
            OccurredAt.AddMinutes(4))!;

        await Assert.That(ReadValue(publication, "AccountableIdentityReference"))
            .IsEqualTo("target-tenant-identity:v2");
    }

    [Test]
    [MethodDataSource(nameof(UnsafeMarkdown))]
    public async Task LocalizedSource_RejectsExecutableTrackingAndRemoteContent(
        string markdown)
    {
        await Assert.That(() => CreateSource(markdown))
            .Throws<TargetInvocationException>();
    }

    public static IEnumerable<string> UnsafeMarkdown()
    {
        yield return "<script>alert('x')</script>";
        yield return "<div>raw html</div>";
        yield return "![remote](https://tracker.example/image.png)";
        yield return "[unsafe](javascript:alert(1))";
        yield return "[tracked](https://example.test/policy?utm_source=x)";
        yield return "[credential](https://user:password@example.test/policy)";
        yield return "[fragment](https://example.test/policy#tracking)";
    }

    [Test]
    public async Task ContentBounds_RejectOversizeLocaleLinkPlaceholderAndPackageShapes()
    {
        string oversizedMarkdown = new('a', 262_145);
        string tooManyLinks = string.Join(
            "\n",
            Enumerable.Range(0, 129)
                .Select(index => $"[link-{index}](https://example.test/{index})"));
        string tooManyPlaceholders = string.Join(
            " ",
            Enumerable.Range(0, 65)
                .Select(index => $"{{{{identity_{index}}}}}"));

        await Assert.That(() => CreateSource(oversizedMarkdown))
            .Throws<TargetInvocationException>();
        await Assert.That(() => CreateSource(tooManyLinks))
            .Throws<TargetInvocationException>();
        await Assert.That(() => CreateSource(tooManyPlaceholders))
            .Throws<TargetInvocationException>();
        await Assert.That(() => CreateSource("# Policy", languageTag: "not a locale"))
            .Throws<TargetInvocationException>();

        object[] locales = Enumerable.Range(0, 33)
            .Select(index => CreateSource(
                "# Policy",
                languageTag: $"x-private-{index}"))
            .ToArray();
        await Assert.That(() => CreateDraft(
                "Instance",
                null,
                "PrivacyNotice",
                locales,
                CreateProvenance("ProjectOwned", "ISLAMU-Internal"),
                "instance-identity:v1"))
            .Throws<TargetInvocationException>();
    }

    [Test]
    public async Task TemplateProvenance_IsNonCertifyingAndRejectsUnreviewedSources()
    {
        object projectOwned = CreateProvenance("ProjectOwned", "ISLAMU-Internal");
        object approvedFoss = CreateProvenance("ApprovedFoss", "MIT");

        foreach (object provenance in new[] { projectOwned, approvedFoss })
        {
            await Assert.That((bool)ReadValue(provenance, "IsLegalAdvice")).IsFalse();
            await Assert.That((bool)ReadValue(provenance, "IsCertification")).IsFalse();
        }

        await Assert.That(() => CreateProvenance("UnreviewedExternal", "unknown"))
            .Throws<ArgumentException>();
    }

    private static object CreateDraft(
        string scope,
        Guid? tenantId,
        string kind,
        object source,
        object? provenance,
        string accountableIdentityReference) =>
        CreateDraft(
            scope,
            tenantId,
            kind,
            [source],
            provenance,
            accountableIdentityReference);

    private static object CreateDraft(
        string scope,
        Guid? tenantId,
        string kind,
        IReadOnlyList<object> sources,
        object? provenance,
        string accountableIdentityReference)
    {
        MethodInfo create = RequireMethod(RequireType("LegalDocument"), "CreateDraft");
        return create.Invoke(
            null,
            [
                Enum.Parse(RequireType("LegalDocumentScope"), scope),
                tenantId,
                Enum.Parse(RequireType("LegalDocumentKind"), kind),
                Enum.Parse(RequireType("LegalDocumentAudience"), "Public"),
                TypedArray(sources),
                provenance,
                accountableIdentityReference,
                false,
                OccurredAt
            ])!;
    }

    private static object CreateSource(
        string markdown,
        string languageTag = "en")
    {
        Type sourceType = RequireType("LegalDocumentLocalizedSource");
        return RequireMethod(sourceType, "Create").Invoke(
            null,
            [
                languageTag,
                "Policy",
                "Portable summary",
                markdown
            ])!;
    }

    private static object CreateProvenance(
        string sourceKind,
        string licenseExpression)
    {
        Type provenanceType = RequireType("LegalDocumentTemplateProvenance");
        Type sourceKindType = RequireType("LegalDocumentTemplateSourceKind");
        if (!Enum.TryParse(sourceKindType, sourceKind, out object? parsed))
            throw new ArgumentException("Unknown template source kind.", nameof(sourceKind));

        return RequireMethod(provenanceType, "Create").Invoke(
            null,
            [
                "template.policy",
                "1.0.0",
                parsed,
                licenseExpression,
                "project-reviewed"
            ])!;
    }

    private static Array TypedArray(object source) => TypedArray([source]);

    private static Array TypedArray(IReadOnlyList<object> sources)
    {
        Type sourceType = RequireType("LegalDocumentLocalizedSource");
        Array array = Array.CreateInstance(sourceType, sources.Count);
        for (int index = 0; index < sources.Count; index++)
            array.SetValue(sources[index], index);
        return array;
    }

    private static object? Invoke(object target, string name, params object?[] arguments) =>
        RequireMethod(target.GetType(), name).Invoke(target, arguments);

    private static MethodInfo RequireMethod(Type type, string name) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .SingleOrDefault(method => string.Equals(
                method.Name,
                name,
                StringComparison.Ordinal))
        ?? throw new InvalidOperationException(
            $"Missing legal document method '{type.FullName}.{name}'.");

    private static Type RequireType(string name) =>
        DomainAssembly.GetType($"Explore.Domain.{name}")
        ?? throw new InvalidOperationException(
            $"Missing legal document contract 'Explore.Domain.{name}'.");

    private static object? ReadValue(object target, string propertyName) =>
        target.GetType()
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?
            .GetValue(target);

    private static IEnumerable ReadEnumerable(object target, string propertyName) =>
        ReadValue(target, propertyName) as IEnumerable
        ?? throw new InvalidOperationException(
            $"Legal document property '{propertyName}' is not enumerable.");

    private static IReadOnlyDictionary<string, object> ReadStaticDictionary(
        Type type,
        string propertyName)
    {
        var values = type.GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.Static)?
            .GetValue(null) as IEnumerable
            ?? throw new InvalidOperationException(
                $"Missing legal document catalog '{type.FullName}.{propertyName}'.");
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (object entry in values)
        {
            string key = entry.GetType().GetProperty("Key")?.GetValue(entry)?.ToString()
                ?? throw new InvalidOperationException("Legal catalog key is missing.");
            object value = entry.GetType().GetProperty("Value")?.GetValue(entry)
                ?? throw new InvalidOperationException("Legal catalog value is missing.");
            result.Add(key, value);
        }

        return result;
    }
}
