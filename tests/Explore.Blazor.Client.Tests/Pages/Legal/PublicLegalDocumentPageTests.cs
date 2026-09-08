namespace Explore.Blazor.Client.Tests.Pages.Legal;

using Explore.Blazor.Client.Contracts.Services.LegalDocuments;
using Explore.Blazor.Client.Pages.Legal;

public sealed class PublicLegalDocumentPageTests : IDisposable
{
    private readonly BlazorTestContext _context = new();
    private readonly ILegalDocumentService _service =
        Substitute.For<ILegalDocumentService>();

    public PublicLegalDocumentPageTests()
    {
        _context.Services.AddSingleton(_service);
    }

    public void Dispose() => _context.Dispose();

    [Test]
    public async Task TermsRoute_RendersRoleLabeledPublishedApiComposition()
    {
        _service.GetAsync(
                "terms-of-service",
                Arg.Any<CancellationToken>())
            .Returns(new PublicLegalDocumentDto
            {
                KindCode = "terms-of-service",
                ScopeCode = "instance",
                OwnerRoleCode = "instance_operator",
                Title = "Published Terms",
                Summary = "Reviewed public terms.",
                LanguageTag = "en",
                RenderedHtml =
                    "<h2>Use of service</h2>\n"
                    + "<p>Read <a href=\"https://example.test/legal\" rel=\"noopener noreferrer\">policy details</a>.</p>\n",
                Version = 3,
                EffectiveAt = new DateTimeOffset(
                    2026,
                    8,
                    30,
                    14,
                    0,
                    0,
                    TimeSpan.Zero),
                ContentDigest = new string('a', 64),
                IsLocaleFallback = false
            });

        var cut = _context.RenderMudComponent<TermsOfService>();
        cut.WaitForAssertion(() =>
        {
            if (cut.FindAll("article.public-legal-document").Count != 1)
                throw new InvalidOperationException("Published legal article was not rendered.");
        });

        await Assert.That(cut.FindAll("h1").Count).IsEqualTo(1);
        await Assert.That(cut.Find("h1").TextContent).IsEqualTo("Terms of Service");
        await Assert.That(cut.FindAll("h2").Select(item => item.TextContent))
            .IsEquivalentTo(["Published Terms", "Use of service"]);
        await Assert.That(cut.Markup).Contains("Instance operator");
        await Assert.That(cut.Find("a").GetAttribute("rel"))
            .IsEqualTo("noopener noreferrer");
        await Assert.That(cut.Markup).DoesNotContain("Description of Service");
    }

    [Test]
    public async Task PrivacyRoute_WhenUnavailableRendersNoSubstituteLegalProse()
    {
        _service.GetAsync(
                "privacy-notice",
                Arg.Any<CancellationToken>())
            .Returns((PublicLegalDocumentDto?)null);

        var cut = _context.RenderMudComponent<PrivacyPolicy>();
        cut.WaitForAssertion(() =>
        {
            if (cut.FindAll("[data-testid='legal-document-unavailable']").Count != 1)
                throw new InvalidOperationException("Unavailable state was not rendered.");
        });

        await Assert.That(cut.FindAll("h1").Count).IsEqualTo(1);
        await Assert.That(cut.Find("h1").TextContent).IsEqualTo("Privacy notice");
        await Assert.That(cut.Markup)
            .Contains("No reviewed public version is currently available.");
        await Assert.That(cut.Markup).DoesNotContain("Information We Collect");
        await Assert.That(cut.FindAll("article").Count).IsEqualTo(0);
    }

    [Test]
    public async Task PublishedFallbackLocale_IsDisclosedWithoutChangingDirectionSafety()
    {
        _service.GetAsync(
                "terms-of-service",
                Arg.Any<CancellationToken>())
            .Returns(new PublicLegalDocumentDto
            {
                KindCode = "terms-of-service",
                ScopeCode = "instance",
                OwnerRoleCode = "instance_operator",
                Title = "الشروط",
                Summary = "نسخة منشورة",
                LanguageTag = "ar",
                RenderedHtml = "<h2>النطاق</h2>\n<p>نص.</p>\n",
                Version = 1,
                EffectiveAt = DateTimeOffset.UtcNow,
                ContentDigest = new string('b', 64),
                IsLocaleFallback = true
            });

        var cut = _context.RenderMudComponent<TermsOfService>();
        cut.WaitForAssertion(() =>
        {
            if (cut.FindAll("article.public-legal-document").Count != 1)
                throw new InvalidOperationException("Published legal article was not rendered.");
        });

        var article = cut.Find("article");
        await Assert.That(article.GetAttribute("lang")).IsEqualTo("ar");
        await Assert.That(article.GetAttribute("dir")).IsEqualTo("auto");
        await Assert.That(cut.Markup).Contains("Language fallback");
    }
}
