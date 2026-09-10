using Bunit.TestDoubles;
using Explore.Blazor.Client.Pages.Onboarding;
using Explore.Blazor.Client.Pages.Onboarding.Components;
using Explore.Blazor.Client.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Refit;
using System.Security.Cryptography;

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
    public async Task KeycloakPublicOnboardingControlsBindSeparateVisitorSignupFields()
    {
        var model = new AuthProviderConfigurationDto
        {
            PrimaryProviderId = 1,
            PrimaryProviderCode = "KEYCLOAK",
            KeycloakAuthority = "https://identity.example.test/realms/events",
            KeycloakClientId = "event-bff",
            KeycloakPublicOnboardingPolicy = PublicOnboardingPolicy.Allowed,
            KeycloakPublicSignupUrl = "https://identity.example.test/realms/events/registrations"
        };
        _onboarding.GetAuthProviderConfigurationAsync().Returns(model);

        var cut = _context.RenderMudComponent<AuthProviderConfiguration>();
        MudSelect<PublicOnboardingPolicy?> policy = cut.FindComponents<MudSelect<PublicOnboardingPolicy?>>()
            .Single(select => select.Instance.Value == PublicOnboardingPolicy.Allowed).Instance;
        MudTextField<string> signupUrl = cut.FindComponents<MudTextField<string>>()
            .Single(field => field.Instance.Value == "https://identity.example.test/realms/events/registrations").Instance;

        await Assert.That(policy.Value).IsEqualTo(PublicOnboardingPolicy.Allowed);
        await Assert.That(signupUrl.Value).IsEqualTo("https://identity.example.test/realms/events/registrations");
        await Assert.That(cut.FindAll(".auth-provider-configuration__visitor-onboarding")).Count().IsEqualTo(2);
        await cut.InvokeAsync(() => policy.ValueChanged.InvokeAsync(PublicOnboardingPolicy.Denied));
        await Assert.That(model.KeycloakPublicOnboardingPolicy).IsEqualTo(PublicOnboardingPolicy.Denied);
    }

    [Test]
    public async Task KeycloakBootstrapAppliesVisitorFieldsToReloadedCompleteConfiguration()
    {
        var initial = new AuthProviderConfigurationDto
        {
            PrimaryProviderId = 1,
            PrimaryProviderCode = "KEYCLOAK",
            KeycloakPublicOnboardingPolicy = PublicOnboardingPolicy.Allowed,
            KeycloakPublicSignupUrl = "https://identity.example.test/registrations",
            GooglePublicOnboardingPolicy = PublicOnboardingPolicy.Denied,
            GooglePublicSignupUrl = "https://accounts.example.test/enroll"
        };
        var canonical = new AuthProviderConfigurationDto
        {
            PrimaryProviderId = 1,
            PrimaryProviderCode = "KEYCLOAK",
            KeycloakAuthority = "https://identity.example.test/realms/events",
            KeycloakClientId = "event-bff",
            AtprotoLoginEnabled = true,
            GoogleSsoEnabled = false
        };
        AuthProviderConfigurationDto? captured = null;
        _onboarding.GetAuthProviderConfigurationAsync().Returns(initial, canonical);
        _onboarding.BootstrapKeycloakRealmAsync(Arg.Any<KeycloakBootstrapRequestDto>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });
        _onboarding.UpdateAuthProviderConfigurationAsAdminAsync(
                Arg.Do<AuthProviderConfigurationDto>(configuration => captured = configuration))
            .Returns(new BaseCommandResponseOfGuid { Success = true });
        var cut = _context.RenderMudComponent<AuthProviderConfiguration>();
        string clientSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        string adminSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        await cut.Find("[data-testid='keycloak-bootstrap-mode']").ClickAsync(new MouseEventArgs());
        foreach (var value in new Dictionary<string, string>
        {
            ["keycloak-bootstrap-base-url"] = "https://identity.example.test",
            ["keycloak-bootstrap-realm"] = "events",
            ["keycloak-bootstrap-client-id"] = "event-bff",
            ["keycloak-bootstrap-client-secret"] = clientSecret,
            ["keycloak-bootstrap-admin-username"] = "bootstrap-admin",
            ["keycloak-bootstrap-admin-password"] = adminSecret
        })
        {
            await cut.Find($"[data-testid='{value.Key}']")
                .InputAsync(new ChangeEventArgs { Value = value.Value });
        }

        await cut.Find("button[data-testid='save-auth-provider-configuration']")
            .ClickAsync(new MouseEventArgs());

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.KeycloakClientId).IsEqualTo("event-bff");
        await Assert.That(captured.AtprotoLoginEnabled).IsTrue();
        await Assert.That(captured.KeycloakPublicOnboardingPolicy).IsEqualTo(PublicOnboardingPolicy.Allowed);
        await Assert.That(captured.KeycloakPublicSignupUrl).IsEqualTo("https://identity.example.test/registrations");
        await Assert.That(captured.GooglePublicOnboardingPolicy).IsEqualTo(PublicOnboardingPolicy.Denied);
        await Assert.That(captured.GooglePublicSignupUrl).IsEqualTo("https://accounts.example.test/enroll");
    }

    [Test]
    public async Task VisitorPolicyConflictReloadsCanonicalProviderConfiguration()
    {
        var attempted = new AuthProviderConfigurationDto
        {
            PrimaryProviderId = 1,
            PrimaryProviderCode = "KEYCLOAK",
            KeycloakAuthority = "https://identity.example.test/realms/events",
            KeycloakClientId = "event-bff",
            KeycloakPublicOnboardingPolicy = PublicOnboardingPolicy.Allowed,
            KeycloakPublicSignupUrl = "https://identity.example.test/registrations"
        };
        var canonical = new AuthProviderConfigurationDto
        {
            PrimaryProviderId = 4,
            PrimaryProviderCode = "LOCAL",
            KeycloakPublicOnboardingPolicy = PublicOnboardingPolicy.Denied
        };
        _onboarding.GetAuthProviderConfigurationAsync().Returns(attempted, canonical);
        _onboarding.UpdateAuthProviderConfigurationAsAdminAsync(attempted)
            .Returns(new BaseCommandResponseOfGuid
            {
                Success = false,
                FailureCode = "visitor_access_account_required_conflict",
                Message = "Existing account-required events conflict with this provider policy."
            });
        var cut = _context.RenderMudComponent<AuthProviderConfiguration>();

        await cut.Find("button[data-testid='save-auth-provider-configuration']")
            .ClickAsync(new MouseEventArgs());

        await Assert.That(cut.FindComponent<MudRadioGroup<int>>().Instance.Value).IsEqualTo(4);
        await Assert.That(cut.FindAll("[role=alert]")).Count().IsGreaterThanOrEqualTo(1);
        await _onboarding.Received(2).GetAuthProviderConfigurationAsync();
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

        await cut.Find("button[data-testid='save-auth-provider-configuration']")
            .ClickAsync(new MouseEventArgs());

        await Assert.That(navigation.Uri).Contains("/login?provider=atproto");
        await Assert.That(navigation.Uri).Contains(
            "returnUrl=%2Fonboarding%2Finstance");
    }

    [Test]
    public async Task SavedLocalPrimaryContinuesSetupWithoutLogin()
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

        await cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Save & Continue Setup", StringComparison.Ordinal))
            .ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        await Assert.That(navigation.Uri).EndsWith("/onboarding/instance");
    }
}
