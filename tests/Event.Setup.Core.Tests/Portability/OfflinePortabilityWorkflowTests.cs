namespace ISLAMU.Setup.Core.Tests.Portability;

using System.Globalization;
using System.Text;
using System.Text.Json;
using ISLAMU.Event.Setup.Core;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public sealed class OfflinePortabilityWorkflowTests
{
    private static readonly SetupProfile Profile = new(
        new SetupProfileIdentity("offline"), [new("configuration"), new("legal")], [new("single")]);

    [Test]
    public async Task CreateValidateExportAndOpenRoundTripBothArtifactKinds()
    {
        SetupSelection manifestSelection = Selection(SetupScope.Instance,
            "instance.settings", "instance.documents", "instance.legal_documents",
            "tenant.settings", "tenant.documents", "tenant.legal_documents");
        SetupSelection packageSelection = Selection(SetupScope.Tenant,
            "tenant.settings", "tenant.documents", "tenant.legal_documents");

        OfflinePortabilityResult manifest = OfflinePortabilityWorkflow.CreateManifest(
            Profile, manifestSelection, "primary-deployment", "default", "Primary Community");
        OfflinePortabilityResult package = OfflinePortabilityWorkflow.CreateTenantPackage(
            Profile, packageSelection, "primary-community", "default", "Primary Community", null);

        await Assert.That(manifest.Succeeded).IsTrue();
        await Assert.That(package.Succeeded).IsTrue();
        await Assert.That(manifest.Document!.State).IsEqualTo(SetupWorkflowState.Draft);
        await Assert.That(package.Document!.Identity.SourceName).IsEqualTo("primary-community");
        await Assert.That(manifest.Document.Sections.Select(item => item.Value))
            .IsEquivalentTo(manifestSelection.Sections.Select(item => item.Value));

        OfflinePortabilityResult readyManifest = OfflinePortabilityWorkflow.Validate(manifest.Document);
        OfflinePortabilityResult readyPackage = OfflinePortabilityWorkflow.Validate(package.Document);
        OfflinePortabilityExportResult exportedManifest = OfflinePortabilityWorkflow.Export(readyManifest.Document!);
        OfflinePortabilityExportResult exportedPackage = OfflinePortabilityWorkflow.Export(readyPackage.Document!);

        await Assert.That(OfflinePortabilityWorkflow.Export(manifest.Document).Succeeded).IsFalse();
        await Assert.That(readyManifest.Document!.State).IsEqualTo(SetupWorkflowState.Ready);
        await Assert.That(exportedManifest.Succeeded).IsTrue();
        await Assert.That(exportedPackage.Succeeded).IsTrue();
        await Assert.That(exportedManifest.Output!.MediaType)
            .IsEqualTo(ConfigurationManifestContractMetadata.MediaType);
        await Assert.That(exportedPackage.Output!.MediaType)
            .IsEqualTo(TenantConfigurationPackageContractMetadata.MediaType);

        OfflinePortabilityResult reopenedManifest = OfflinePortabilityWorkflow.OpenManifest(
            Profile, manifestSelection, exportedManifest.Output.Bytes);
        OfflinePortabilityResult reopenedPackage = OfflinePortabilityWorkflow.OpenTenantPackage(
            Profile, packageSelection, exportedPackage.Output.Bytes);
        await Assert.That(OfflinePortabilityWorkflow.Format(reopenedManifest.Document!).Output!.Bytes.ToArray())
            .IsEquivalentTo(exportedManifest.Output!.Bytes.ToArray());
        await Assert.That(OfflinePortabilityWorkflow.Format(reopenedPackage.Document!).Output!.Bytes.ToArray())
            .IsEquivalentTo(exportedPackage.Output!.Bytes.ToArray());
        await Assert.That(reopenedManifest.Document!.Identity).IsEqualTo(readyManifest.Document!.Identity);
        await Assert.That(reopenedPackage.Document!.Identity).IsEqualTo(readyPackage.Document!.Identity);
    }

    [Test]
    public async Task CreateRejectsWireInvalidDisplayBoundsWithoutReturningDraft()
    {
        SetupSelection manifestSelection = Selection(SetupScope.Tenant, "tenant.settings");
        SetupSelection packageSelection = Selection(SetupScope.Tenant, "tenant.settings");
        string boundary = new('x', LegalMarkdownContentLimits.MaximumSummaryLength);
        string tooLong = boundary + "x";

        OfflinePortabilityResult validManifest = OfflinePortabilityWorkflow.CreateManifest(
            Profile, manifestSelection, "manifest", "tenant", boundary);
        OfflinePortabilityResult validPackage = OfflinePortabilityWorkflow.CreateTenantPackage(
            Profile, packageSelection, "package", "tenant", boundary, null);
        OfflinePortabilityResult invalidManifest = OfflinePortabilityWorkflow.CreateManifest(
            Profile, manifestSelection, "manifest", "tenant", tooLong);
        OfflinePortabilityResult invalidPackage = OfflinePortabilityWorkflow.CreateTenantPackage(
            Profile, packageSelection, "package", "tenant", tooLong, null);

        await Assert.That(validManifest.Succeeded).IsTrue();
        await Assert.That(validPackage.Succeeded).IsTrue();
        await Assert.That(invalidManifest.Succeeded).IsFalse();
        await Assert.That(invalidManifest.Document).IsNull();
        await Assert.That(invalidPackage.Succeeded).IsFalse();
        await Assert.That(invalidPackage.Document).IsNull();
        await Assert.That(invalidManifest.Diagnostics.Single().Code.Value)
            .IsEqualTo(ConfigurationPortabilityDiagnosticCodes.StringTooLong);
        await Assert.That(invalidPackage.Diagnostics.Single().Code.Value)
            .IsEqualTo(ConfigurationPortabilityDiagnosticCodes.StringTooLong);
    }

    [Test]
    public async Task OpenUsesStrictWireFailuresWithoutPartialDocumentsOrValues()
    {
        SetupSelection selection = Selection(SetupScope.Instance, "instance.settings");
        byte[] valid = ConfigurationPortabilityJsonCodec.SerializeConfigurationManifest(MinimalManifest());
        string text = Encoding.UTF8.GetString(valid);
        byte[][] invalid =
        [
            Encoding.UTF8.GetBytes("{"),
            Encoding.UTF8.GetBytes(text.Replace("\"metadata\":", "\"unknown\":true,\"metadata\":", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(text.Replace("\"settings\":{}", "\"settings\":{\"password\":\"not-a-value\"}", StringComparison.Ordinal)),
            new byte[ConfigurationPortabilityContentLimits.MaximumArtifactUtf8Bytes + 1]
        ];

        foreach (byte[] bytes in invalid)
        {
            OfflinePortabilityResult result = OfflinePortabilityWorkflow.OpenManifest(Profile, selection, bytes);
            await Assert.That(result.Succeeded).IsFalse();
            await Assert.That(result.Document).IsNull();
            await Assert.That(result.Diagnostics).IsNotEmpty();
            await Assert.That(result.Diagnostics.All(item => item.GetType().GetProperties()
                .Select(property => property.Name).Order().SequenceEqual(DiagnosticMembers))).IsTrue();
        }

        byte[] wrongScope = Encoding.UTF8.GetBytes(text.Replace(
            "\"documents\":{},\"legalDocuments\":{}",
            "\"documents\":{\"tenant.branding\":{\"schemaVersion\":1,\"payload\":{}}},\"legalDocuments\":{}",
            StringComparison.Ordinal));
        OfflinePortabilityResult scopeResult = OfflinePortabilityWorkflow.OpenManifest(Profile, selection, wrongScope);
        await Assert.That(scopeResult.Diagnostics.Single().Code.Value)
            .IsEqualTo(ConfigurationPortabilityDiagnosticCodes.ScopeInvalid);
    }

    [Test]
    public async Task EditIsImmutableSingleSectionAndRejectsUnknownOrExcludedSections()
    {
        SetupSelection selection = Selection(SetupScope.Tenant,
            "tenant.settings", "tenant.documents", "tenant.legal_documents");
        OfflinePortabilityDocument original = OfflinePortabilityWorkflow.CreateTenantPackage(
            Profile, selection, "community", "source-community", "Community", "source-instance").Document!;
        JsonElement enabled = JsonSerializer.SerializeToElement(true);
        OfflinePortabilitySectionSnapshot settings = OfflinePortabilitySectionSnapshot.Settings(
            new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["events.require_approval"] = enabled });

        OfflinePortabilityResult edited = OfflinePortabilityWorkflow.Edit(
            original, new OfflinePortabilitySectionEdit(new("tenant.settings"), settings));
        await Assert.That(edited.Succeeded).IsTrue();
        await Assert.That(edited.Document!.State).IsEqualTo(SetupWorkflowState.Draft);
        await Assert.That(original.TenantPackage!.Spec.Settings).IsEmpty();
        await Assert.That(edited.Document.TenantPackage!.Spec.Settings).Count().IsEqualTo(1);
        await Assert.That(edited.Document.Identity).IsEqualTo(original.Identity);

        OfflinePortabilityResult removed = OfflinePortabilityWorkflow.Edit(
            edited.Document, OfflinePortabilitySectionEdit.Remove(new("tenant.settings")));
        await Assert.That(removed.Document!.TenantPackage!.Spec.Settings).IsEmpty();
        foreach (string key in ExcludedSections.Append("unknown.section"))
        {
            OfflinePortabilityResult rejected = OfflinePortabilityWorkflow.Edit(
                original, new OfflinePortabilitySectionEdit(new(key), settings));
            await Assert.That(rejected.Succeeded).IsFalse();
            await Assert.That(rejected.Document).IsNull();
        }
    }

    [Test]
    public async Task FormatDiffCoverageAndDigestsAreCanonicalOrdinalAndCultureIndependent()
    {
        SetupSelection selection = Selection(SetupScope.Tenant,
            "tenant.settings", "tenant.documents", "tenant.legal_documents");
        OfflinePortabilityDocument first = OfflinePortabilityWorkflow.Validate(
            OfflinePortabilityWorkflow.CreateTenantPackage(Profile, selection,
                "community", "source-community", "Community", null).Document!).Document!;
        OfflinePortabilityOutput baseline = OfflinePortabilityWorkflow.Format(first).Output!;
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            OfflinePortabilityOutput repeated = OfflinePortabilityWorkflow.Format(first).Output!;
            await Assert.That(repeated.Bytes.ToArray()).IsEquivalentTo(baseline.Bytes.ToArray());
            await Assert.That(repeated.Digest).IsEqualTo(baseline.Digest);
        }
        finally { CultureInfo.CurrentCulture = original; }

        OfflinePortabilityDocument changed = OfflinePortabilityWorkflow.Edit(first,
            new OfflinePortabilitySectionEdit(new("tenant.settings"),
                OfflinePortabilitySectionSnapshot.Settings(new Dictionary<string, JsonElement>
                { ["events.require_approval"] = JsonSerializer.SerializeToElement(true) }))).Document!;
        SetupDiffResult diff = OfflinePortabilityWorkflow.Diff(first, changed);
        SetupCoverageResult coverage = OfflinePortabilityWorkflow.Coverage(changed);
        await Assert.That(diff.Changed.Select(item => item.Value)).IsEquivalentTo(["tenant.settings"]);
        await Assert.That(coverage.Covered.Select(item => item.Value))
            .IsEquivalentTo(selection.Sections.Select(item => item.Value));
        await Assert.That(coverage.IsComplete).IsTrue();
        await Assert.That(baseline.ToString()).DoesNotContain("Community");
    }

    [Test]
    public async Task DiffIgnoresSourceIdentityAndChangesOnlyEditedSection()
    {
        SetupSelection selection = Selection(SetupScope.Tenant,
            "tenant.settings", "tenant.documents", "tenant.legal_documents");
        OfflinePortabilityDocument first = OfflinePortabilityWorkflow.CreateTenantPackage(
            Profile, selection, "first-source", "first-tenant", "First display", "first-instance").Document!;
        OfflinePortabilityDocument second = OfflinePortabilityWorkflow.CreateTenantPackage(
            Profile, selection, "second-source", "second-tenant", "Second display", "second-instance").Document!;

        SetupDiffResult identityOnly = OfflinePortabilityWorkflow.Diff(first, second);
        await Assert.That(identityOnly.Changed).IsEmpty();
        await Assert.That(identityOnly.Unchanged.Select(item => item.Value))
            .IsEquivalentTo(selection.Sections.Select(item => item.Value));

        OfflinePortabilityDocument edited = OfflinePortabilityWorkflow.Edit(second,
            new OfflinePortabilitySectionEdit(new("tenant.settings"),
                OfflinePortabilitySectionSnapshot.Settings(new Dictionary<string, JsonElement>
                { ["events.require_approval"] = JsonSerializer.SerializeToElement(true) }))).Document!;
        SetupDiffResult content = OfflinePortabilityWorkflow.Diff(first, edited);
        await Assert.That(content.Changed.Select(item => item.Value)).IsEquivalentTo(["tenant.settings"]);
        await Assert.That(content.Unchanged.Select(item => item.Value))
            .IsEquivalentTo(["tenant.documents", "tenant.legal_documents"]);
    }

    [Test]
    public async Task LegalValueObjectsAndTransitiveWorkflowProjectionsDoNotExposeValues()
    {
        const string sourceSentinel = "sentinel-source";
        const string titleSentinel = "SENTINEL-TITLE";
        const string summarySentinel = "SENTINEL-SUMMARY";
        const string markdownSentinel = "SENTINEL-MARKDOWN";
        const string templateSentinel = "SENTINEL-TEMPLATE";
        const string licenseSentinel = "SENTINEL-LICENSE";
        const string reviewSentinel = "SENTINEL-REVIEW";
        OfflineLegalLocale locale = OfflineLegalLocale.Create(
            "en", titleSentinel, summarySentinel, "# " + markdownSentinel);
        OfflineLegalDraftProvenance provenance = OfflineLegalDraftProvenance.ProjectOwned(
            templateSentinel, "1", licenseSentinel, reviewSentinel);
        OfflinePortabilityResult created = OfflinePortabilityWorkflow.CreateTenantPackage(
            Profile, Selection(SetupScope.Tenant, "tenant.settings"),
            sourceSentinel, "source-tenant", "Display", null);

        string[] projections =
        [
            created.Document!.Identity.ToString(), created.Document.ToString(), created.ToString(),
            provenance.ToString(), locale.ToString()
        ];
        string[] sentinels =
        [
            sourceSentinel, titleSentinel, summarySentinel, markdownSentinel,
            templateSentinel, licenseSentinel, reviewSentinel
        ];
        foreach (string projection in projections)
            foreach (string sentinel in sentinels)
                await Assert.That(projection).DoesNotContain(sentinel);
    }

    [Test]
    public async Task EveryLegalKindHasExplicitRoleCorrectScopeClassification()
    {
        OfflineLegalLocale locale = OfflineLegalLocale.Create("en", "Title", "Summary", "# Source");
        HashSet<OfflineLegalDocumentKind> tenantKinds =
        [
            OfflineLegalDocumentKind.TenantTerms,
            OfflineLegalDocumentKind.TenantPrivacyNotice,
            OfflineLegalDocumentKind.TenantCodeOfConduct,
            OfflineLegalDocumentKind.OrganizerSubmissionTerms,
            OfflineLegalDocumentKind.EventPublicationModerationPolicy,
            OfflineLegalDocumentKind.CancellationRefundPolicy,
            OfflineLegalDocumentKind.RegistrationParticipantPrivacyNotice,
            OfflineLegalDocumentKind.MediaPhotographyNotice,
            OfflineLegalDocumentKind.SafeguardingMinorParticipationPolicy,
            OfflineLegalDocumentKind.VenueAccessibilityPolicy,
            OfflineLegalDocumentKind.ComplaintCorrectionCopyrightNotice,
            OfflineLegalDocumentKind.SponsorshipPartnerDisclosure,
            OfflineLegalDocumentKind.TenantRetentionContactSharingNotice
        ];

        foreach (OfflineLegalDocumentKind kind in Enum.GetValues<OfflineLegalDocumentKind>())
        {
            OfflineLegalDraftScope expectedScope = tenantKinds.Contains(kind)
                ? OfflineLegalDraftScope.Tenant : OfflineLegalDraftScope.Instance;
            OfflineLegalDraftRole expectedRole = expectedScope == OfflineLegalDraftScope.Tenant
                ? OfflineLegalDraftRole.TenantDraftAuthority : OfflineLegalDraftRole.InstanceDraftAuthority;
            OfflineLegalDraft accepted = OfflineLegalDraft.Create(expectedScope, kind,
                OfflineLegalAudience.Public, false, [], null, OfflineLegalDraftProvenance.Blank, [locale]);
            await Assert.That(accepted.Role).IsEqualTo(expectedRole);
            await Assert.That(() => OfflineLegalDraft.Create(
                    expectedScope == OfflineLegalDraftScope.Tenant
                        ? OfflineLegalDraftScope.Instance : OfflineLegalDraftScope.Tenant,
                    kind, OfflineLegalAudience.Public, false, [], null,
                    OfflineLegalDraftProvenance.Blank, [locale]))
                .Throws<ArgumentException>();
        }
    }

    [Test]
    public async Task LegalDraftsDeriveRolePreserveReviewedSourceAndFailClosedOnAuthorityOrUnsafeMarkdown()
    {
        OfflineLegalLocale locale = OfflineLegalLocale.Create(
            "EN-us", "Community policy", "Review required", "# Policy\r\n\r\nOperator: {{accountable_identity}}.");
        OfflineLegalDraft draft = OfflineLegalDraft.Create(
            OfflineLegalDraftScope.Tenant, OfflineLegalDocumentKind.TenantTerms,
            OfflineLegalAudience.Public, true, ["local review"], "New source",
            OfflineLegalDraftProvenance.ApprovedFoss("template", "1", "MIT", "reviewed"), [locale]);

        await Assert.That(draft.Role).IsEqualTo(OfflineLegalDraftRole.TenantDraftAuthority);
        await Assert.That(draft.Localizations.Single().LanguageTag).IsEqualTo("en-us");
        await Assert.That(draft.Localizations.Single().Markdown).DoesNotContain("\r");
        await Assert.That(draft.ToWire().LifecycleIntent).IsEqualTo("Draft");
        await Assert.That(draft.ToWire().ProposedEffectiveAt).IsNull();
        await Assert.That(draft.ToWire().AccountableIdentityReference).IsNull();
        await Assert.That(draft.ToWire().RequiresFreshAcceptance).IsTrue();

        OfflineLegalPreview notReady = draft.Preview("en-US", new Dictionary<string, string>());
        await Assert.That(notReady.IsReady).IsFalse();
        await Assert.That(notReady.Html).IsEmpty();
        await Assert.That(notReady.Diagnostics.Single().Code.Value)
            .IsEqualTo(LegalMarkdownDiagnosticCodes.IdentityUnresolved);

        SetupSelection legalSelection = Selection(SetupScope.Tenant, "tenant.legal_documents");
        OfflinePortabilityDocument package = OfflinePortabilityWorkflow.CreateTenantPackage(
            Profile, legalSelection, "community", "source-community", "Community", null).Document!;
        OfflinePortabilityDocument withLegal = OfflinePortabilityWorkflow.Edit(package,
            new OfflinePortabilitySectionEdit(new("tenant.legal_documents"),
                OfflinePortabilitySectionSnapshot.LegalDocuments(
                    new Dictionary<string, ConfigurationManifestLegalDocumentV1Alpha2>
                    { [draft.DocumentKey] = draft.ToWire() }))).Document!;
        OfflinePortabilityResult unresolvedExport = OfflinePortabilityWorkflow.Validate(withLegal);
        await Assert.That(unresolvedExport.Succeeded).IsFalse();
        await Assert.That(unresolvedExport.Document).IsNull();
        await Assert.That(unresolvedExport.Diagnostics.Single().Code.Value).IsEqualTo("legal-draft-invalid");

        await Assert.That(() => OfflineLegalDraft.Create(
            OfflineLegalDraftScope.Instance, OfflineLegalDocumentKind.TenantTerms,
            OfflineLegalAudience.Public, false, [], null, OfflineLegalDraftProvenance.Blank, [locale]))
            .Throws<ArgumentException>();
        await Assert.That(() => OfflineLegalLocale.Create(
            "en", "Title", "Summary", "# Policy\n\n<div>unsafe</div>"))
            .Throws<ArgumentException>();
        await Assert.That(() => OfflineLegalLocale.Create(
            "en--us", "Title", "Summary", "# Source"))
            .Throws<ArgumentException>();
        await Assert.That(() => OfflineLegalDraft.FromWire(OfflineLegalDraftScope.Tenant,
            OfflineLegalDocumentKind.TenantTerms.ToString(), draft.ToWire() with
            { LifecycleIntent = "Published", ProposedEffectiveAt = DateTime.UnixEpoch }))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task PublicWorkflowClosureContainsNoDotenvOrTargetAuthority()
    {
        string[] forbidden = ["Dotenv", "EnvironmentValue", "Secret", "Provider", "Topology",
            "Target", "Apply", "Mapping", "Live", "AcceptanceRecord", "PublishedAt", "UserId", "TenantId"];
        Type[] closure = typeof(OfflinePortabilityWorkflow).Assembly.GetExportedTypes()
            .Where(type => type.Name.StartsWith("OfflinePortability", StringComparison.Ordinal)
                || type.Name.StartsWith("OfflineLegal", StringComparison.Ordinal))
            .ToArray();
        string[] publicMembers = closure.SelectMany(type => type.GetMembers()
            .Select(member => type.Name + "." + member.Name)).ToArray();
        foreach (string fragment in forbidden)
            await Assert.That(publicMembers.Any(name => name.Contains(fragment, StringComparison.OrdinalIgnoreCase))).IsFalse();
    }

    private static SetupSelection Selection(SetupScope scope, params string[] keys) =>
        new(scope, ConfigurationImportApplyMode.PreviewOnly, keys.Select(key => new PortableSectionKey(key)));

    private static ConfigurationManifestV1Alpha2 MinimalManifest() => new()
    {
        Schema = ConfigurationManifestContractMetadata.SchemaId,
        ApiVersion = ConfigurationManifestContractMetadata.ApiVersion,
        Kind = ConfigurationManifestContractMetadata.Kind,
        Metadata = new ConfigurationManifestMetadataV1Alpha2 { Name = "primary-deployment" },
        Spec = new ConfigurationManifestSpecV1Alpha2
        {
            Instance = new ConfigurationManifestInstanceV1Alpha2
            { Settings = new Dictionary<string, JsonElement>(), Documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(), LegalDocuments = new Dictionary<string, ConfigurationManifestLegalDocumentV1Alpha2>() },
            Tenants = []
        }
    };

    private static readonly string[] DiagnosticMembers = ["Code", "Path", "Severity"];

    private static readonly string[] ExcludedSections =
    [
        "excluded.secrets", "excluded.pii", "excluded.application_data", "excluded.operational_state",
        "excluded.provider_bindings", "excluded.deployment_topology", "dotenv", "environment.catalogue",
        "tenant.footer", "tenant.navigation", "tenant.templates", "tenant.lookups",
        "tenant.custom_property_definitions", "tenant.localization", "tenant.registration_policy",
        "tenant.modules", "extensions"
    ];
}
