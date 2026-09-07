using Bunit.TestDoubles;
using Explore.Blazor.Client.Pages.Onboarding;
using Explore.Blazor.Client.Pages.Onboarding.Components;
using Explore.Blazor.Client.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Refit;

namespace Explore.Blazor.Client.Tests.Pages.Onboarding;

public sealed class AuthProviderConfigurationTests : IDisposable
{
    private readonly BlazorTestContext _context = new();
    private readonly IInstanceOnboardingService _onboarding =
        Substitute.For<IInstanceOnboardingService>();

    public AuthProviderConfigurationTests()
    {
        _context.Services.AddSingleton(_onboarding);
        _context.Services.AddSingleton(Substitute.For<IBffAuthApi>());
        _context.Services.AddSingleton(
            Substitute.For<ILogger<AuthProviderConfiguration>>());
        _onboarding.GetStatusAsync().Returns(
            new InstanceOnboardingStatusDto { IsCompleted = false });
        _onboarding.ShouldSkipAuthorizationProviderStepAsync().Returns(true);
    }

    public void Dispose() => _context.Dispose();

    [Test]
    public async Task UnsetProviderDefaultsToLocalWithIndependentAtprotoChoice()
    {
        _onboarding.GetAuthProviderConfigurationAsync()
            .Returns(new AuthProviderConfigurationDto());

        var cut = _context.RenderMudComponent<AuthProviderConfiguration>();
        var providerGroup = cut.FindComponent<MudRadioGroup<int>>();

        await Assert.That(providerGroup.Instance.Value).IsEqualTo(4);
        await Assert.That(cut.FindAll("[data-testid=onboarding-primary-local]"))
            .Count().IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid=onboarding-primary-keycloak]"))
            .Count().IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid=onboarding-primary-atproto]"))
            .Count().IsEqualTo(1);
        await Assert.That(cut.Markup).Contains("Enable AT Protocol Login");
        await Assert.That(cut.Markup).DoesNotContain(
            "At least one authentication provider must be enabled");
        await Assert.That(cut.FindAll("h1")).Count().IsEqualTo(1);
        await Assert.That(cut.FindComponent<OnboardingWorkspace>()).IsNotNull();
    }

    [Test]
    public async Task KeycloakSelectionRevealsConfigurationWithoutChangingAtproto()
    {
        _onboarding.GetAuthProviderConfigurationAsync()
            .Returns(new AuthProviderConfigurationDto
            {
                PrimaryProviderId = 4,
                PrimaryProviderCode = "LOCAL",
                AtprotoLoginEnabled = true
            });
        var cut = _context.RenderMudComponent<AuthProviderConfiguration>();

        await cut.Find("[data-testid=onboarding-primary-keycloak]")
            .ClickAsync(new MouseEventArgs());

        await Assert.That(cut.Markup).Contains("Authority URL (Required)");
        await Assert.That(cut.Markup).Contains("Enable AT Protocol Login");
        await Assert.That(cut.Markup).Contains("Public URL (Required)");
        await Assert.That(cut.FindComponent<MudRadioGroup<int>>().Instance.Value)
            .IsEqualTo(1);
    }

    [Test]
    public async Task AtprotoSelectionForcesPasswordlessSoleProviderState()
    {
        var model = new AuthProviderConfigurationDto
        {
            PrimaryProviderId = 4,
            PrimaryProviderCode = "local",
            PrimaryProviderName = "Local Identity",
            AtprotoLoginEnabled = false,
            GoogleSsoEnabled = true,
            GoogleClientId = "client.apps.googleusercontent.com"
        };
        _onboarding.GetAuthProviderConfigurationAsync().Returns(model);
        var cut = _context.RenderMudComponent<AuthProviderConfiguration>();

        await cut.Find("[data-testid=onboarding-primary-atproto]")
            .ClickAsync(new MouseEventArgs());

        await Assert.That(model.PrimaryProviderId).IsEqualTo(2);
        await Assert.That(model.PrimaryProviderCode).IsEqualTo("ATPROTO");
        await Assert.That(model.PrimaryProviderName).IsEqualTo("AT Protocol");
        await Assert.That(model.AtprotoLoginEnabled).IsTrue();
        await Assert.That(model.GoogleSsoEnabled).IsFalse();
        await Assert.That(cut.Markup).DoesNotContain("Authority URL (Required)");
    }

    [Test]
    public async Task SavedAtprotoPrimaryRequiresFocusedLoginBeforeContinuing()
    {
        var model = new AuthProviderConfigurationDto
        {
            PrimaryProviderId = 2,
            PrimaryProviderCode = "atproto",
            PrimaryProviderName = "AT Protocol",
            AtprotoLoginEnabled = true,
            AtprotoPublicUrl = "https://events.example.test"
        };
        _onboarding.GetAuthProviderConfigurationAsync().Returns(model);
        _onboarding.UpdateAuthProviderConfigurationAsAdminAsync(model)
            .Returns(new BaseCommandResponseOfGuid
            {
                Success = true
            });
        var authApi = Substitute.For<IBffAuthApi>();
        var refresh = Substitute.For<IApiResponse>();
        refresh.IsSuccessStatusCode.Returns(true);
        authApi.RefreshSchemesAsync(Arg.Any<CancellationToken>())
            .Returns(refresh);
        _context.Services.AddSingleton(authApi);
        var navigation =
            _context.Services.GetRequiredService<BunitNavigationManager>();
        var cut = _context.RenderMudComponent<AuthProviderConfiguration>();

        cut.FindAll("button")
            .Single(button => button.TextContent.Contains(
                "Save & Continue to Login",
                StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            if (!navigation.Uri.Contains(
                    "/login?provider=atproto",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "AT Protocol onboarding did not route through focused login.");
            }
        });
        await Assert.That(navigation.Uri).Contains(
            "returnUrl=%2Fonboarding%2Finstance");
    }

    [Test]
    public async Task SavedLocalPrimaryRequiresLoginBeforeContinuing()
    {
        var model = new AuthProviderConfigurationDto
        {
            PrimaryProviderId = 4,
            PrimaryProviderCode = "local",
            PrimaryProviderName = "Local Identity"
        };
        _onboarding.GetAuthProviderConfigurationAsync().Returns(model);
        _onboarding.UpdateAuthProviderConfigurationAsAdminAsync(model)
            .Returns(new BaseCommandResponseOfGuid
            {
                Success = true
            });
        var authApi = Substitute.For<IBffAuthApi>();
        var refresh = Substitute.For<IApiResponse>();
        refresh.IsSuccessStatusCode.Returns(true);
        authApi.RefreshSchemesAsync(Arg.Any<CancellationToken>())
            .Returns(refresh);
        _context.Services.AddSingleton(authApi);
        var navigation =
            _context.Services.GetRequiredService<BunitNavigationManager>();
        var cut = _context.RenderMudComponent<AuthProviderConfiguration>();

        cut.FindAll("button")
            .Single(button => button.TextContent.Contains(
                "Save & Continue to Login",
                StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            if (!navigation.Uri.Contains(
                    "/login?provider=local",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Local onboarding did not route through focused login.");
            }
        });
        await Assert.That(navigation.Uri).Contains(
            "returnUrl=%2Fonboarding%2Finstance");
    }
}
