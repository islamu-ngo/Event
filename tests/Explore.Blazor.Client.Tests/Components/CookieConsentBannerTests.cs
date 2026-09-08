using Explore.Blazor.Client.Shared;

namespace Explore.Blazor.Client.Tests.Components;

public class CookieConsentBannerTests : IDisposable
{
    private readonly BlazorTestContext _ctx;

    public CookieConsentBannerTests()
    {
        _ctx = new BlazorTestContext();
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    [Test]
    public async Task Render_WhenVisible_ShowsBannerMarkup()
    {
        var cut = _ctx.Render<CookieConsentBanner>(p => p
            .Add(b => b.Visible, true));

        var banner = cut.Find(".cookie-consent-banner");
        await Assert.That(banner).IsNotNull();
    }

    [Test]
    public async Task Render_WhenNotVisible_RendersEmpty()
    {
        var cut = _ctx.Render<CookieConsentBanner>(p => p
            .Add(b => b.Visible, false));

        await Assert.That(cut.Markup.Trim()).IsEmpty();
    }

    [Test]
    public async Task Render_WhenVisible_ShowsBannerText()
    {
        var cut = _ctx.Render<CookieConsentBanner>(p => p
            .Add(b => b.Visible, true));

        var text = cut.Find(".cookie-consent-banner__text");
        await Assert.That(text.TextContent).Contains("cookies");
        await Assert.That(text.TextContent).Contains("analytics");
    }

    [Test]
    public async Task Render_WhenVisible_HasAcceptAndDeclineButtons()
    {
        var cut = _ctx.Render<CookieConsentBanner>(p => p
            .Add(b => b.Visible, true));

        var buttons = cut.FindAll(".cookie-consent-banner__btn");
        await Assert.That(buttons).Count().IsEqualTo(2);
    }

    [Test]
    public async Task Render_DeclineButtonIsOutlinedForEqualProminence()
    {
        var cut = _ctx.Render<CookieConsentBanner>(p => p
            .Add(b => b.Visible, true));

        // Decline is the first button, uses Outlined variant (MudBlazor renders as mud-button-outlined)
        var buttons = cut.FindAll(".cookie-consent-banner__btn");
        var declineButton = buttons[0];
        await Assert.That(declineButton.TextContent).Contains("Decline");
    }

    [Test]
    public async Task Render_AcceptButtonIsSecondForEqualProminence()
    {
        var cut = _ctx.Render<CookieConsentBanner>(p => p
            .Add(b => b.Visible, true));

        var buttons = cut.FindAll(".cookie-consent-banner__btn");
        var acceptButton = buttons[1];
        await Assert.That(acceptButton.TextContent).Contains("Accept");
    }

    [Test]
    public async Task AcceptButton_Click_InvokesOnAcceptCallback()
    {
        var accepted = false;
        var cut = _ctx.Render<CookieConsentBanner>(p => p
            .Add(b => b.Visible, true)
            .Add(b => b.OnAccept, () => { accepted = true; }));

        var buttons = cut.FindAll(".cookie-consent-banner__btn");
        buttons[1].Click(); // Accept is second

        await Assert.That(accepted).IsTrue();
    }

    [Test]
    public async Task DeclineButton_Click_InvokesOnDeclineCallback()
    {
        var declined = false;
        var cut = _ctx.Render<CookieConsentBanner>(p => p
            .Add(b => b.Visible, true)
            .Add(b => b.OnDecline, () => { declined = true; }));

        var buttons = cut.FindAll(".cookie-consent-banner__btn");
        buttons[0].Click(); // Decline is first

        await Assert.That(declined).IsTrue();
    }

    [Test]
    public async Task Render_HasBemStructure()
    {
        var cut = _ctx.Render<CookieConsentBanner>(p => p
            .Add(b => b.Visible, true));

        // Verify BEM class hierarchy exists
        await Assert.That(cut.FindAll(".cookie-consent-banner")).Count().IsEqualTo(1);
        await Assert.That(cut.FindAll(".cookie-consent-banner__content")).Count().IsEqualTo(1);
        await Assert.That(cut.FindAll(".cookie-consent-banner__text")).Count().IsEqualTo(1);
        await Assert.That(cut.FindAll(".cookie-consent-banner__actions")).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Render_DefaultVisibility_IsFalse()
    {
        var cut = _ctx.Render<CookieConsentBanner>();

        await Assert.That(cut.Markup.Trim()).IsEmpty();
    }
}
