using System.Globalization;
using Bunit;
using Bunit.TestDoubles;
using Explore.Blazor.Client.Models;
using Explore.Blazor.Client.Providers;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explore.Blazor.Client.Tests.Providers;

public sealed class LanguageProviderTests
{
    [Test]
    [Arguments("ar", true)]
    [Arguments("fr", false)]
    public async Task FirstRenderUsesRequestCultureBeforePersistingHydrationState(string culture, bool rtl)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            using var context = new BlazorTestContext();
            var state = context.AddBunitPersistentComponentState();
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            context.JSInterop.Setup<string?>("localization.getLanguageCookie").SetResult(culture);
            var host = context.Render<LanguageProvider>(parameters =>
                parameters.AddChildContent<LanguageProbe>());

            var probe = host.FindComponent<LanguageProbe>().Instance;
            await Assert.That(probe.InitialLanguage).IsEqualTo(culture);
            await Assert.That(probe.InitialRtl).IsEqualTo(rtl);
            state.TriggerOnPersisting();
            await Assert.That(state.TryTake<string>("culture-state", out var persisted)).IsTrue();
            await Assert.That(persisted).IsEqualTo(culture);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    [Test]
    public async Task FirstRenderPreloadDoesNotOverwriteNewerSelectedLanguageAndDirection()
    {
        var timeout = TimeSpan.FromSeconds(5);
        using var context = new BlazorTestContext();
        context.AddBunitPersistentComponentState();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.Setup<string?>("localization.getLanguageCookie").SetResult("en");
        context.JSInterop.Setup<string?>("localization.getDirectionCookie").SetResult(null);
        context.JSInterop.SetupVoid("localization.setDirection", _ => true).SetVoidResult();

        var client = Substitute.For<ITranslationClient>();
        var englishRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var englishResponse = new TaskCompletionSource<IDictionary<string, string>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.GetTranslationByLanguageAsync("en", null, null, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                englishRequested.SetResult();
                return englishResponse.Task;
            });
        client.GetTranslationByLanguageAsync("ar", null, null, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string>());

        using var translation = new TranslationService(client, NullLogger<TranslationService>.Instance);
        var preload = new GatedPreload(translation);
        context.Services.AddSingleton<ITranslationService>(preload);

        var host = context.Render<ObservedLanguageProvider>(parameters =>
            parameters.AddChildContent<LanguageProbe>());
        var probe = host.FindComponent<LanguageProbe>().Instance;

        try
        {
            await englishRequested.Task.WaitAsync(timeout);
            englishResponse.SetResult(new Dictionary<string, string>());
            await preload.NativePreloadCompleted.WaitAsync(timeout);

            await host.InvokeAsync(() => translation.ChangeLanguageAsync("ar")).WaitAsync(timeout);
            host.WaitForState(() => probe.Language is { LanguageCode: "ar", EffectiveIsRtl: true }, timeout);
        }
        finally
        {
            englishResponse.TrySetResult(new Dictionary<string, string>());
            preload.Release();
        }

        await host.Instance.FirstRenderCompleted.WaitAsync(timeout);
        var selected = await host.InvokeAsync(() => probe.Language);
        await Assert.That(selected?.LanguageCode).IsEqualTo("ar");
        await Assert.That(selected?.EffectiveIsRtl == true).IsTrue();
    }

    // The native transport lock also gates ChangeLanguageAsync. Hold the preload
    // return to the provider after the native service has released that lock.
    private sealed class GatedPreload(TranslationService translation) : ITranslationService
    {
        private readonly TaskCompletionSource _nativePreloadCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task NativePreloadCompleted => _nativePreloadCompleted.Task;

        public void Release() => _release.SetResult();

        public string CurrentLanguage => translation.CurrentLanguage;

        public event Action<string>? OnLanguageChanged
        {
            add => translation.OnLanguageChanged += value;
            remove => translation.OnLanguageChanged -= value;
        }

        public string T(string key, string? fallback = null) => translation.T(key, fallback);

        public Task<IDictionary<string, string>> GetTranslationsAsync(string languageCode, CancellationToken ct) =>
            translation.GetTranslationsAsync(languageCode, ct);

        public Task<ICollection<string>> GetAvailableLanguagesAsync(CancellationToken ct) =>
            translation.GetAvailableLanguagesAsync(ct);

        public Task ChangeLanguageAsync(string languageCode, CancellationToken ct) =>
            translation.ChangeLanguageAsync(languageCode, ct);

        public async Task PreloadAsync(string languageCode, CancellationToken ct)
        {
            await translation.PreloadAsync(languageCode, ct);
            _nativePreloadCompleted.SetResult();
            await _release.Task.WaitAsync(ct);
        }
    }

    public sealed class ObservedLanguageProvider : LanguageProvider
    {
        private readonly TaskCompletionSource _firstRenderCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FirstRenderCompleted => _firstRenderCompleted.Task;

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            await base.OnAfterRenderAsync(firstRender);
            if (firstRender)
                _firstRenderCompleted.SetResult();
        }
    }

    public sealed class LanguageProbe : ComponentBase
    {
        [CascadingParameter(Name = "Language")]
        public LanguageContext? Language { get; set; }

        public string? InitialLanguage { get; private set; }
        public bool InitialRtl { get; private set; }

        protected override void OnParametersSet()
        {
            if (InitialLanguage is null && Language is { } language)
            {
                InitialLanguage = language.LanguageCode;
                InitialRtl = language.EffectiveIsRtl;
            }
        }
    }
}
