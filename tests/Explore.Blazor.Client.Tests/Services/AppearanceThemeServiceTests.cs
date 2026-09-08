using System.Net;
using System.Net.Http.Json;
using MudBlazor;
using MudBlazor.Utilities;
using ClientAvailablePresetDto = Explore.Blazor.Client.Clients.AvailablePresetDto;
using ClientCreateCustomProfileRequestDto = Explore.Blazor.Client.Clients.CreateCustomProfileRequestDto;
using ClientPaletteDto = Explore.Blazor.Client.Clients.UiThemePaletteDto;
using ClientResolvedAppearanceDto = Explore.Blazor.Client.Clients.ResolvedAppearanceDto;
using ClientResolvedThemeDto = Explore.Blazor.Client.Clients.ResolvedThemeDto;
using ClientUserAppearanceProfileDto = Explore.Blazor.Client.Clients.UserAppearanceProfileDto;

namespace Explore.Blazor.Client.Tests.Services;

public class AppearanceThemeServiceTests
{
    [Test]
    public async Task CreateTheme_SetsExpectedAppbarHeightAndPalette()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var theme = service.CreateTheme("112px");

        await Assert.That(theme.LayoutProperties.AppbarHeight).IsEqualTo("112px");
        await Assert.That(theme.LayoutProperties.DefaultBorderRadius).IsEqualTo("12px");
        await Assert.That(theme.Typography?.Default?.FontFamily?.FirstOrDefault()).IsEqualTo("Inter");
        await Assert.That(theme.PaletteLight.Success.ToString(MudColorOutputFormats.Hex).ToUpperInvariant()).IsEqualTo("#047857");
        await Assert.That(theme.PaletteLight.Warning.ToString(MudColorOutputFormats.Hex).ToUpperInvariant()).IsEqualTo("#B45309");
        await Assert.That(theme.PaletteLight.ActionDisabled.ToString(MudColorOutputFormats.Hex).ToUpperInvariant()).IsEqualTo("#52525B");
        await Assert.That(theme.PaletteLight.ActionDisabledBackground.ToString(MudColorOutputFormats.Hex).ToUpperInvariant()).IsEqualTo("#E4E4E7");
        await Assert.That(theme.PaletteLight.TextDisabled.ToString(MudColorOutputFormats.Hex).ToUpperInvariant()).IsEqualTo("#52525B");
    }

    [Test]
    public async Task ResolveEffectiveDarkModeAsync_ReturnsDarkForDarkMode()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK));
        service.Current.ThemeMode = "dark";

        var result = await service.ResolveEffectiveDarkModeAsync(null!);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task ResolveEffectiveDarkModeAsync_ReturnsLightForLightMode()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK));
        service.Current.ThemeMode = "light";

        var result = await service.ResolveEffectiveDarkModeAsync(null!);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ResolveEffectiveDarkModeAsync_ReturnsDarkForDarkHighContrastMode()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK));
        service.Current.ThemeMode = "darkhighcontrast";

        var result = await service.ResolveEffectiveDarkModeAsync(null!);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task ResolveEffectiveDarkModeAsync_ReturnsLightForLightHighContrastMode()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK));
        service.Current.ThemeMode = "lighthighcontrast";

        var result = await service.ResolveEffectiveDarkModeAsync(null!);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ResolveEffectiveDarkModeAsync_ReturnsServerHintForSystemMode()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK));
        service.Current.ThemeMode = "system";
        service.Current.ServerEffectiveDarkMode = true;

        var result = await service.ResolveEffectiveDarkModeAsync(null!);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Current_InitialState_HasSystemThemeMode()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Assert.That(service.Current.ThemeMode).IsEqualTo("system");
    }

    [Test]
    public async Task GeneratePalettePreview_ReturnsFallbackOnFailure()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var palette = service.GeneratePalettePreview("#475569", "#3B82F6", false);

        await Assert.That(palette).IsNotNull();
        await Assert.That(palette.Primary).IsEqualTo("#18181B");
        await Assert.That(palette.PrimaryContrastText).IsEqualTo("#FFFFFF");
    }

    [Test]
    public async Task GeneratePalettePreview_ReturnsFallbackForDarkOnFailure()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var palette = service.GeneratePalettePreview("#475569", "#3B82F6", true);

        await Assert.That(palette).IsNotNull();
        await Assert.That(palette.Primary).IsEqualTo("#FAFAFA");
        await Assert.That(palette.PrimaryContrastText).IsEqualTo("#1A1A1A");
    }

    [Test]
    public async Task GeneratePalettePreview_DoesNotBlockOnHttpPreviewEndpoint()
    {
        var requestCount = 0;
        var service = CreateService(_ =>
        {
            requestCount++;
            return CreateJsonResponse(CreatePalette(primary: "#ABCDEF"));
        });

        var palette = service.GeneratePalettePreview("#475569", "#3B82F6", false);

        await Assert.That(palette.Primary).IsEqualTo("#18181B");
        await Assert.That(requestCount).IsEqualTo(0);
    }

    [Test]
    public async Task CreateTheme_PreservesCustomPaletteContrastText()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK));
        service.Current.ResolvedAppearance = new ClientResolvedAppearanceDto
        {
            Theme = new ClientResolvedThemeDto
            {
                LightPalette = new ClientPaletteDto
                {
                    Primary = "#123456",
                    PrimaryContrastText = "#FEDCBA",
                    Secondary = "#654321",
                    SecondaryContrastText = "#ABCDEF"
                },
                DarkPalette = new ClientPaletteDto
                {
                    Primary = "#FAFAFA",
                    PrimaryContrastText = "#1A1A1A",
                    Secondary = "#A1A1AA",
                    SecondaryContrastText = "#101010"
                }
            }
        };

        var theme = service.CreateTheme("64px");

        await Assert.That(theme.PaletteLight.PrimaryContrastText.ToString(MudColorOutputFormats.Hex).ToUpperInvariant()).IsEqualTo("#FEDCBA");
        await Assert.That(theme.PaletteLight.SecondaryContrastText.ToString(MudColorOutputFormats.Hex).ToUpperInvariant()).IsEqualTo("#ABCDEF");
        await Assert.That(theme.PaletteDark.PrimaryContrastText.ToString(MudColorOutputFormats.Hex).ToUpperInvariant()).IsEqualTo("#1A1A1A");
        await Assert.That(theme.PaletteDark.SecondaryContrastText.ToString(MudColorOutputFormats.Hex).ToUpperInvariant()).IsEqualTo("#101010");
    }

    [Test]
    public async Task InitializeAsync_LoadsResolvedAppearancePresetsAndProfilesFromBff()
    {
        var requests = new List<string>();
        var service = CreateService(request =>
        {
            requests.Add($"{request.Method} {request.RequestUri?.PathAndQuery}");

            return request.RequestUri?.PathAndQuery switch
            {
                "/api/user/appearance" => CreateJsonResponse(new ClientResolvedAppearanceDto
                {
                    ThemeMode = "darkhighcontrast",
                    Direction = "rtl",
                    Language = "fr",
                    ServerEffectiveDarkMode = true
                }),
                "/api/user/appearance/presets" => CreateJsonResponse<IReadOnlyList<ClientAvailablePresetDto>>(
                    [CreatePreset()]),
                "/api/user/appearance/profiles" => CreateJsonResponse<IReadOnlyList<ClientUserAppearanceProfileDto>>(
                    [CreateProfile(Guid.Parse("10000000-0000-0000-0000-000000000001"))]),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });

        await service.InitializeAsync(null!);

        await Assert.That(service.Current.ThemeMode).IsEqualTo("darkhighcontrast");
        await Assert.That(service.Current.Direction).IsEqualTo("rtl");
        await Assert.That(service.Current.Language).IsEqualTo("fr");
        await Assert.That(service.Current.AvailablePresets.Count).IsEqualTo(1);
        await Assert.That(service.Current.UserProfiles.Count).IsEqualTo(1);
        await Assert.That(requests).Contains("GET /api/user/appearance");
        await Assert.That(requests).Contains("GET /api/user/appearance/presets");
        await Assert.That(requests).Contains("GET /api/user/appearance/profiles");
    }

    [Test]
    public async Task ClonePresetAndActivateAsync_WithLegacyMalformedRgba_KeepsPresetsAfterReinitialize()
    {
        var requests = new List<string>();
        var preset = CreatePreset();
        var presetId = Guid.Parse("30000000-0000-0000-0000-000000000003");
        var profile = CreateProfile(Guid.Parse("20000000-0000-0000-0000-000000000002"));
        var darkPalette = CreatePalette(isDark: true);
        darkPalette.AppbarBackground = "#RGBA(9,9,11,0.92)";
        var appearanceReadCount = 0;

        var service = CreateService(request =>
        {
            var path = request.RequestUri?.PathAndQuery;
            requests.Add($"{request.Method} {path}");

            if (request.Method == HttpMethod.Get && path == "/api/user/appearance")
            {
                appearanceReadCount++;
                return CreateJsonResponse(new ClientResolvedAppearanceDto
                {
                    ThemeMode = "system",
                    Theme = new ClientResolvedThemeDto
                    {
                        LightPalette = CreatePalette(),
                        DarkPalette = appearanceReadCount == 1 ? CreatePalette(isDark: true) : darkPalette
                    }
                });
            }

            if (request.Method == HttpMethod.Get && path == "/api/user/appearance/presets")
            {
                return CreateJsonResponse<IReadOnlyList<ClientAvailablePresetDto>>([preset]);
            }

            if (request.Method == HttpMethod.Get && path == "/api/user/appearance/profiles")
            {
                return CreateJsonResponse<IReadOnlyList<ClientUserAppearanceProfileDto>>([]);
            }

            if (request.Method == HttpMethod.Post
                && path == $"/api/user/appearance/profiles/from-preset/{presetId}")
            {
                return CreateJsonResponse(profile);
            }

            if (request.Method == HttpMethod.Put && path == "/api/user/appearance/active-profile")
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        await service.InitializeAsync(null!);
        IReadOnlyList<ClientAvailablePresetDto> observedPresets = [];
        MudTheme? rebuiltTheme = null;
        service.Changed += (_, _) => rebuiltTheme = service.CreateTheme("64px");
        service.Changed += (_, args) => observedPresets = args.State.AvailablePresets;

        await service.ClonePresetAndActivateAsync(presetId);
        await service.InitializeAsync(null!);

        await Assert.That(service.Current.ThemeMode).IsEqualTo("system");
        await Assert.That(service.Current.AvailablePresets.Count).IsEqualTo(1);
        await Assert.That(observedPresets.Select(item => item.ThemeKey)).Contains("standard");
        await Assert.That(rebuiltTheme).IsNotNull();
        await Assert.That(rebuiltTheme!.PaletteDark.AppbarBackground.ToString(MudColorOutputFormats.RGBA))
            .Contains("9,9,11");
        await Assert.That(string.Join("\n", requests)).IsEqualTo(string.Join("\n", new[]
        {
            "GET /api/user/appearance",
            "GET /api/user/appearance/presets",
            "GET /api/user/appearance/profiles",
            $"POST /api/user/appearance/profiles/from-preset/{presetId}",
            "PUT /api/user/appearance/active-profile",
            "GET /api/user/appearance"
        }));
    }

    [Test]
    public async Task CreateCustomProfileAsync_PostsToBffAndReturnsProfile()
    {
        HttpRequestMessage? capturedRequest = null;
        var profileId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var service = CreateService(request =>
        {
            capturedRequest = request;
            return CreateJsonResponse(CreateProfile(profileId));
        });

        var result = await service.CreateCustomProfileAsync(new ClientCreateCustomProfileRequestDto
        {
            Name = "Custom",
            NaturalColor = "#111111",
            BrandColor = "#222222"
        });

        await Assert.That(result?.Id).IsEqualTo(profileId);
        await Assert.That(capturedRequest?.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(capturedRequest?.RequestUri?.PathAndQuery).IsEqualTo("/api/user/appearance/profiles");
    }

    [Test]
    public async Task GeneratePalettePreviewAsync_EscapesQueryAndReturnsPalette()
    {
        HttpRequestMessage? capturedRequest = null;
        var service = CreateService(request =>
        {
            capturedRequest = request;
            return CreateJsonResponse(CreatePalette(primary: "#ABCDEF"));
        });

        var result = await service.GeneratePalettePreviewAsync("soft gray", "brand blue", isDark: false);

        await Assert.That(result?.Primary).IsEqualTo("#ABCDEF");
        await Assert.That(capturedRequest?.Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(capturedRequest?.RequestUri?.AbsoluteUri).Contains("naturalColor=soft%20gray");
        await Assert.That(capturedRequest?.RequestUri?.AbsoluteUri).Contains("brandColor=brand%20blue");
    }

    private static AppearanceThemeService CreateService(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        var logger = Substitute.For<ILogger<AppearanceThemeService>>();
        var client = new HttpClient(new StubHttpMessageHandler(responseFactory))
        {
            BaseAddress = new Uri("https://example.test/")
        };

        return new AppearanceThemeService(
            new UserAppearanceClient(client),
            new UiThemeAdminClient(client),
            logger);
    }

    private static HttpResponseMessage CreateJsonResponse<T>(T value) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(value)
    };

    private static ClientAvailablePresetDto CreatePreset() => new()
    {
        Id = Guid.Parse("30000000-0000-0000-0000-000000000003"),
        ThemeKey = "standard",
        DisplayName = "Standard",
        LightPalette = CreatePalette(),
        DarkPalette = CreatePalette(isDark: true)
    };

    private static ClientUserAppearanceProfileDto CreateProfile(Guid id) => new()
    {
        Id = id,
        Name = "Profile",
        LightPaletteSnapshot = CreatePalette(),
        DarkPaletteSnapshot = CreatePalette(isDark: true)
    };

    private static ClientPaletteDto CreatePalette(string primary = "#123456", bool isDark = false) => new()
    {
        Primary = primary,
        PrimaryContrastText = isDark ? "#1A1A1A" : "#FFFFFF",
        Secondary = "#654321",
        SecondaryContrastText = isDark ? "#101010" : "#ABCDEF",
        Background = isDark ? "#1A1A1A" : "#F5F5F7",
        Surface = isDark ? "#242424" : "#FFFFFF",
        AppbarBackground = isDark ? "rgba(26,26,26,0.92)" : "#FFFFFF",
        AppbarText = isDark ? "#FAFAFA" : "#18181B",
        DrawerBackground = isDark ? "#1A1A1A" : "#FFFFFF",
        DrawerText = isDark ? "#FAFAFA" : "#18181B",
        DrawerIcon = isDark ? "#A1A1AA" : "#52525B",
        TextPrimary = isDark ? "#FAFAFA" : "#18181B",
        TextSecondary = isDark ? "#A1A1AA" : "#404040",
        Info = "#52525B",
        Success = "#16A34A",
        Warning = "#D97706",
        Error = "#DC2626",
        LinesDefault = isDark ? "#3F3F46" : "#A1A1AA",
        Divider = isDark ? "#2E2E2E" : "#E4E4E7"
    };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }
}
