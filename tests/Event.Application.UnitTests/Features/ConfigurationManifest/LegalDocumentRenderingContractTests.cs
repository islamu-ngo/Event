namespace Event.Application.UnitTests.Features.ConfigurationManifest;

using Explore.Application.Features.ConfigurationManifest.LegalDocuments;
using Explore.Domain;

public sealed class LegalDocumentRenderingContractTests
{
    private static readonly DateTime OccurredAt =
        new(2026, 8, 30, 14, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task PreviewAndPublishedComposition_ProduceIdenticalSafeHtml()
    {
        var service = new LegalDocumentRenderingService();
        LegalDocumentLocalizedSource source = Source(
            "en",
            "# Policy\n\nAccountable operator: {{accountable_identity}}.");
        var identities = IdentityValues("Operator & Community");
        LegalDocumentRenderView preview =
            service.RenderPreview(source, "en", identities);
        LegalDocument document = PublishedDocument(source);

        LegalDocumentRenderView published =
            service.RenderLastPublished(document, "en", identities);

        await Assert.That(preview.IsReady).IsTrue();
        await Assert.That(published.IsReady).IsTrue();
        await Assert.That(published.Html).IsEqualTo(preview.Html);
        await Assert.That(published.OwnerRole)
            .IsEqualTo(LegalDocumentOwnerRole.InstanceOperator);
        await Assert.That(published.Version).IsEqualTo(1);
    }

    [Test]
    public async Task DraftRevision_DoesNotReplaceLastPublishedContent()
    {
        var service = new LegalDocumentRenderingService();
        LegalDocument document = PublishedDocument(
            Source("en", "# Published\n\nStable public source."));
        document.CreateRevision(
            LegalDocumentAudience.Public,
            [
                Source(
                    "en",
                    "# Draft\n\nAccountable operator: {{missing_identity}}.")
            ],
            templateProvenance: null,
            requiresFreshAcceptance: false,
            OccurredAt.AddMinutes(5));

        LegalDocumentRenderView publicView = service.RenderLastPublished(
            document,
            "en",
            IdentityValues("Operator"));

        await Assert.That(publicView.IsReady).IsTrue();
        await Assert.That(publicView.Html).Contains("Stable public source.");
        await Assert.That(publicView.Html).DoesNotContain("missing_identity");
        await Assert.That(publicView.Version).IsEqualTo(1);
    }

    [Test]
    public async Task PublishedComposition_UsesDeterministicLocaleFallback()
    {
        var service = new LegalDocumentRenderingService();
        LegalDocument document = PublishedDocument(
            Source("en", "# English\n\nDefault."),
            Source("fr", "# Français\n\nTexte."));

        LegalDocumentRenderView result = service.RenderLastPublished(
            document,
            "fr-BE",
            IdentityValues("Operator"));

        await Assert.That(result.IsReady).IsTrue();
        await Assert.That(result.LanguageTag).IsEqualTo("fr");
        await Assert.That(result.Diagnostics.Select(item => item.Code))
            .Contains("legal_document_locale_fallback");
    }

    [Test]
    public async Task ImportedUnreviewedDocument_ReturnsValueSafeBlockersOnly()
    {
        var service = new LegalDocumentRenderingService();
        LegalDocument imported = LegalDocument.CreateImportedDraft(
            LegalDocumentScope.Instance,
            tenantId: null,
            LegalDocumentKind.PrivacyNotice,
            LegalDocumentAudience.Public,
            [Source("en", "# Imported\n\nPortable source.")],
            templateProvenance: null,
            "source-origin:digest",
            requiresFreshAcceptance: false,
            OccurredAt);

        LegalDocumentRenderView result = service.RenderLastPublished(
            imported,
            "en",
            IdentityValues("Operator"));

        await Assert.That(result.IsReady).IsFalse();
        await Assert.That(result.Html).IsEmpty();
        await Assert.That(result.Diagnostics.Select(item => item.Code))
            .IsEquivalentTo(["legal_document_not_published"]);
        await Assert.That(result.Diagnostics.Select(item => item.Subject))
            .All(item => item is null);
    }

    [Test]
    public async Task PublishedNonPublicAudience_NeverProducesPublicHtml()
    {
        var service = new LegalDocumentRenderingService();
        LegalDocument document = PublishedDocument(
            LegalDocumentAudience.Administrators,
            Source("en", "# Internal\n\nAdministrator source."));

        LegalDocumentRenderView result = service.RenderLastPublished(
            document,
            "en",
            IdentityValues("Operator"));

        await Assert.That(result.IsReady).IsFalse();
        await Assert.That(result.Html).IsEmpty();
        await Assert.That(result.Diagnostics.Select(item => item.Code))
            .IsEquivalentTo(["legal_document_not_public"]);
    }

    private static LegalDocument PublishedDocument(
        params LegalDocumentLocalizedSource[] sources) =>
        PublishedDocument(LegalDocumentAudience.Public, sources);

    private static LegalDocument PublishedDocument(
        LegalDocumentAudience audience,
        params LegalDocumentLocalizedSource[] sources)
    {
        LegalDocument document = LegalDocument.CreateDraft(
            LegalDocumentScope.Instance,
            tenantId: null,
            LegalDocumentKind.TermsOfService,
            audience,
            sources,
            templateProvenance: null,
            "instance-identity:v1",
            requiresFreshAcceptance: false,
            OccurredAt);
        document.SubmitForReview(OccurredAt.AddMinutes(1));
        document.Approve(
            Guid.CreateVersion7(),
            "review:evidence",
            OccurredAt.AddMinutes(2));
        document.Schedule(
            OccurredAt.AddMinutes(3),
            OccurredAt.AddMinutes(2));
        document.Publish(OccurredAt.AddMinutes(3));
        return document;
    }

    private static LegalDocumentLocalizedSource Source(
        string languageTag,
        string markdown) =>
        LegalDocumentLocalizedSource.Create(
            languageTag,
            "Policy",
            "Public summary",
            markdown);

    private static IReadOnlyDictionary<string, string> IdentityValues(
        string accountableIdentity) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["accountable_identity"] = accountableIdentity
        };
}
