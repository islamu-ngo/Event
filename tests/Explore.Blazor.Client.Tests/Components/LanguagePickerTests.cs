using System.Reflection;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Shared;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Blazor.Client.Tests.Components;

public sealed class LanguagePickerTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private readonly ITranslationService _translationService;
    private readonly ILanguagePreferenceService _languagePreferenceService;
    private readonly IAccessibilityAnnouncerService _announcer;

    public LanguagePickerTests()
    {
        _ctx = new BlazorTestContext();

        _translationService = Substitute.For<ITranslationService>();
        _translationService.CurrentLanguage.Returns("en");
        _translationService.ChangeLanguageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _languagePreferenceService = Substitute.For<ILanguagePreferenceService>();
        _languagePreferenceService.SetLanguageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        _announcer = Substitute.For<IAccessibilityAnnouncerService>();
        _announcer.AnnounceAssertiveAsync(Arg.Any<string>()).Returns(Task.CompletedTask);

        _ctx.Services.RemoveAll<ITranslationService>();
        _ctx.Services.RemoveAll<ILanguagePreferenceService>();
        _ctx.Services.RemoveAll<IAccessibilityAnnouncerService>();
        _ctx.Services.AddSingleton(_translationService);
        _ctx.Services.AddSingleton(_languagePreferenceService);
        _ctx.Services.AddSingleton(_announcer);
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task Render_WhenDisabled_RendersNothing()
    {
        var cut = RenderPicker(enabled: false);

        await Assert.That(cut.FindAll(".language-picker")).IsEmpty();
        await Assert.That(cut.Markup).DoesNotContain("Change language");
    }

    [Test]
    public async Task Render_WithLanguageContext_UsesAccessibleCurrentLanguageLabel()
    {
        var cut = RenderPicker(LanguageContext.ForLanguage("ar"));

        var button = cut.Find("button[aria-label='Change language. Current: العربية']");

        await Assert.That(button).IsNotNull();
        await Assert.That(button.TextContent).Contains("AR");
    }

    [Test]
    public async Task SelectLanguageAsync_WhenPreferencePersists_ChangesTranslationLanguage()
    {
        var cut = RenderPicker();
        var picker = cut.FindComponent<LanguagePicker>();

        await InvokeSelectLanguageAsync(picker.Instance, "fr");

        await _languagePreferenceService.Received(1).SetLanguageAsync("fr", Arg.Any<CancellationToken>());
        await _translationService.Received(1).ChangeLanguageAsync("fr", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SelectLanguageAsync_WhenPreferenceFails_DoesNotChangeTranslationsAndAnnouncesFailure()
    {
        _languagePreferenceService.SetLanguageAsync("fr", Arg.Any<CancellationToken>())
            .Returns(false);
        var cut = RenderPicker();
        var picker = cut.FindComponent<LanguagePicker>();

        await InvokeSelectLanguageAsync(picker.Instance, "fr");

        await _languagePreferenceService.Received(1).SetLanguageAsync("fr", Arg.Any<CancellationToken>());
        await _translationService.DidNotReceiveWithAnyArgs().ChangeLanguageAsync(default!, default);
        await _announcer.Received(1).AnnounceAssertiveAsync("Language change failed.");
    }

    [Test]
    public async Task SelectLanguageAsync_WhenLanguageAlreadyCurrent_DoesNotCallServices()
    {
        var cut = RenderPicker(LanguageContext.ForLanguage("en"));
        var picker = cut.FindComponent<LanguagePicker>();

        await InvokeSelectLanguageAsync(picker.Instance, "en");

        await _languagePreferenceService.DidNotReceiveWithAnyArgs().SetLanguageAsync(default!, default);
        await _translationService.DidNotReceiveWithAnyArgs().ChangeLanguageAsync(default!, default);
    }

    private IRenderedComponent<CascadingValue<LanguageContext>> RenderPicker(
        LanguageContext? language = null,
        bool enabled = true)
    {
        return _ctx.RenderMudComponent<CascadingValue<LanguageContext>>(parameters => parameters
            .Add(component => component.Name, "Language")
            .Add(component => component.Value, language ?? LanguageContext.ForLanguage("en"))
            .AddChildContent<LanguagePicker>(child => child
                .Add(component => component.Enabled, enabled)));
    }

    private static Task InvokeSelectLanguageAsync(LanguagePicker picker, string languageCode)
    {
        var method = typeof(LanguagePicker).GetMethod(
            "SelectLanguageAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        return (Task)method!.Invoke(picker, [languageCode])!;
    }
}
