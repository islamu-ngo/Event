using Explore.Blazor.Client.Contracts.Interop;
using Explore.Blazor.Client.Pages.Admin.Instance.Components;
using Explore.Blazor.Client.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using System.Security.Cryptography;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class InstanceAuthProviderSectionTests : IDisposable
{
    private readonly BlazorTestContext _context = new();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    public InstanceAuthProviderSectionTests()
    {
        _context.Services.AddSingleton(_dialogs);
        _context.Services.AddSingleton(Substitute.For<IBrowserActionInterop>());
    }

    public void Dispose() => _context.Dispose();

    [Test]
    public async Task LocalPrimaryRendersExclusiveSelectorAndIndependentAtprotoControl()
    {
        AuthProviderConfigurationDto model = LocalModel();

        var cut = Render(model);

        await Assert.That(cut.FindAll("[data-testid=primary-provider-local]"))
            .Count().IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid=primary-provider-keycloak]"))
            .Count().IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid=primary-provider-atproto]"))
            .Count().IsEqualTo(1);
        await Assert.That(cut.Markup).Contains("Local Identity");
        await Assert.That(cut.Markup).Contains("AT Protocol Login");
        await Assert.That(cut.Markup).Contains("Disable AT Protocol Login");
        await Assert.That(model.PrimaryProviderId).IsEqualTo(4);
    }

    [Test]
    public async Task PublicOnboardingControlsRemainSeparateAndBindCompleteProviderModel()
    {
        AuthProviderConfigurationDto model = LocalModel();
        model.KeycloakPublicOnboardingPolicy = PublicOnboardingPolicy.Allowed;
        model.KeycloakPublicSignupUrl = "https://identity.example.test/realms/events/registrations";
        model.GooglePublicOnboardingPolicy = PublicOnboardingPolicy.Denied;

        var cut = Render(model);
        var policies = cut.FindComponents<MudSelect<PublicOnboardingPolicy?>>();
        MudSelect<PublicOnboardingPolicy?> keycloakPolicy = policies.Single(select => select.Instance.Value == PublicOnboardingPolicy.Allowed).Instance;
        MudSelect<PublicOnboardingPolicy?> googlePolicy = policies.Single(select => select.Instance.Value == PublicOnboardingPolicy.Denied).Instance;

        await Assert.That(cut.FindAll(".instance-auth-provider__visitor-onboarding")).Count().IsEqualTo(2);
        await cut.InvokeAsync(() => googlePolicy.ValueChanged.InvokeAsync(PublicOnboardingPolicy.Allowed));
        await Assert.That(model.GooglePublicOnboardingPolicy).IsEqualTo(PublicOnboardingPolicy.Allowed);
    }

    [Test]
    public async Task KeycloakCannotBeSelectedBeforeItsTargetConfigurationExists()
    {
        AuthProviderConfigurationDto model = LocalModel();
        _dialogs.ShowMessageBoxAsync(
                Arg.Any<MessageBoxOptions>(),
                Arg.Any<DialogOptions>())
            .Returns(true);
        var cut = Render(model);

        await cut.Find("[data-testid=primary-provider-keycloak]")
            .ClickAsync(new MouseEventArgs());

        await Assert.That(model.PrimaryProviderId).IsEqualTo(4);
        await _dialogs.Received(1).ShowMessageBoxAsync(
            Arg.Is<MessageBoxOptions>(options =>
                options.Title == "Prepare Keycloak first"),
            Arg.Any<DialogOptions>());
    }

    [Test]
    public async Task PreparedKeycloakSwitchRequiresConfirmationAndUpdatesPrimaryIdentity()
    {
        AuthProviderConfigurationDto model = LocalModel();
        model.KeycloakAuthority = "https://identity.example.test/realms/event";
        model.KeycloakClientId = "event-bff";
        model.KeycloakClientSecret = Convert.ToBase64String(
            RandomNumberGenerator.GetBytes(32));
        _dialogs.ShowMessageBoxAsync(
                Arg.Any<MessageBoxOptions>(),
                Arg.Any<DialogOptions>())
            .Returns(true);
        var cut = Render(model);

        await cut.Find("[data-testid=primary-provider-keycloak]")
            .ClickAsync(new MouseEventArgs());

        await Assert.That(model.PrimaryProviderId).IsEqualTo(1);
        await Assert.That(model.PrimaryProviderCode).IsEqualTo("KEYCLOAK");
        await Assert.That(model.PrimaryProviderName).IsEqualTo("Keycloak");
        await Assert.That(model.AtprotoLoginEnabled).IsTrue();
    }

    [Test]
    public async Task AtprotoPrimaryLocksItsRequiredAxisAndSuppressesGoogle()
    {
        var model = new AuthProviderConfigurationDto
        {
            PrimaryProviderId = 2,
            PrimaryProviderCode = "ATPROTO",
            PrimaryProviderName = "AT Protocol",
            AtprotoLoginEnabled = true,
            AtprotoPublicUrl = "https://events.example.test",
            GoogleSsoEnabled = true
        };

        var cut = Render(model);

        await Assert.That(cut.FindComponent<MudRadioGroup<int>>()
                .Instance.Value)
            .IsEqualTo(2);
        MudSwitch<bool> requiredAtprotoToggle = cut
            .FindComponents<MudSwitch<bool>>()
            .Select(component => component.Instance)
            .Single(component => component.Disabled && component.Value);
        await Assert.That(requiredAtprotoToggle.Disabled).IsTrue();
        await Assert.That(cut.Markup).Contains(
            "AT Protocol is the sole primary authority");
        await Assert.That(cut.Markup).DoesNotContain("Google Client ID");
    }

    [Test]
    public async Task ConfirmedAtprotoSwitchSetsSoleProviderState()
    {
        AuthProviderConfigurationDto model = LocalModel();
        model.GoogleSsoEnabled = true;
        _dialogs.ShowMessageBoxAsync(
                Arg.Any<MessageBoxOptions>(),
                Arg.Any<DialogOptions>())
            .Returns(true);
        var cut = Render(model);

        await cut.Find("[data-testid=primary-provider-atproto]")
            .ClickAsync(new MouseEventArgs());

        await Assert.That(model.PrimaryProviderId).IsEqualTo(2);
        await Assert.That(model.PrimaryProviderCode).IsEqualTo("ATPROTO");
        await Assert.That(model.PrimaryProviderName)
            .IsEqualTo("AT Protocol");
        await Assert.That(model.AtprotoLoginEnabled).IsTrue();
        await Assert.That(model.GoogleSsoEnabled).IsFalse();
    }

    [Test]
    public async Task AtprotoCannotBeSelectedWithoutPublicUrl()
    {
        AuthProviderConfigurationDto model = LocalModel();
        model.AtprotoPublicUrl = string.Empty;
        _dialogs.ShowMessageBoxAsync(
                Arg.Any<MessageBoxOptions>(),
                Arg.Any<DialogOptions>())
            .Returns(true);
        var cut = Render(model);

        await cut.Find("[data-testid=primary-provider-atproto]")
            .ClickAsync(new MouseEventArgs());

        await Assert.That(model.PrimaryProviderId).IsEqualTo(4);
        await _dialogs.Received(1).ShowMessageBoxAsync(
            Arg.Is<MessageBoxOptions>(options =>
                options.Title == "Prepare AT Protocol first"),
            Arg.Any<DialogOptions>());
    }

    private IRenderedComponent<InstanceAuthProviderSection> Render(
        AuthProviderConfigurationDto model) =>
        _context.Render<InstanceAuthProviderSection>(parameters => parameters
            .Add(component => component.Model, model)
            .Add(
                component => component.AuthorizationModel,
                new AuthorizationProviderConfigurationDto
                {
                    Provider = "local",
                    AuthorizationProviderManagedByDeployment = false
                })
            .Add(component => component.IsSingleTenant, true));

    private static AuthProviderConfigurationDto LocalModel() => new()
    {
        PrimaryProviderId = 4,
        PrimaryProviderCode = "LOCAL",
        PrimaryProviderName = "Local Identity",
        AtprotoLoginEnabled = true,
        AtprotoPublicUrl = "https://events.example.test",
        GoogleSsoEnabled = false
    };
}
