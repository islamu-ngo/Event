using Explore.Blazor.Client.Pages.Legal;
using Explore.Blazor.Client.Services;

namespace Explore.Blazor.Client.Tests.Pages.Legal;

public sealed class CommunityGuidelinesTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IPublicExperienceService _publicExperienceService = Substitute.For<IPublicExperienceService>();

    public CommunityGuidelinesTests()
    {
        _ctx.Services.AddSingleton(_publicExperienceService);
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    [Test]
    public async Task Render_WhenGuidelinesContainDangerousHtml_EscapesTenantContent()
    {
        _publicExperienceService.GetCachedSettingsAsync().Returns(new PublicExperienceSettingsDto
        {
            CommunityGuidelinesContent = """
                # <script>alert(1)</script>
                Welcome **<img src=x onerror=alert(1)>** to the platform.
                - Use javascript:alert(1) only as text.
                """
        });

        var cut = _ctx.RenderMudComponent<CommunityGuidelines>();

        cut.WaitForAssertion(() =>
        {
            if (cut.FindAll(".community-guidelines-content").Count != 1)
            {
                throw new InvalidOperationException("Expected community guidelines content to render.");
            }
        });

        var content = cut.Find(".community-guidelines-content");
        await Assert.That(content.TextContent).Contains("<script>alert(1)</script>");
        await Assert.That(content.TextContent).Contains("<img src=x onerror=alert(1)>");
        await Assert.That(content.InnerHtml).Contains("&lt;script&gt;alert(1)&lt;/script&gt;");
        await Assert.That(content.InnerHtml).Contains("&lt;img src=x onerror=alert(1)&gt;");
        await Assert.That(content.QuerySelectorAll("strong").Length).IsEqualTo(1);
        await Assert.That(content.QuerySelectorAll("script").Length).IsEqualTo(0);
        await Assert.That(content.QuerySelectorAll("img").Length).IsEqualTo(0);
        await Assert.That(content.QuerySelectorAll("[onerror]").Length).IsEqualTo(0);
    }
}
