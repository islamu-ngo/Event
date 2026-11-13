using System.Globalization;
using Bunit;
using Bunit.TestDoubles;
using Explore.Blazor.Client.Models;
using Explore.Blazor.Client.Providers;
using Microsoft.AspNetCore.Components;

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
