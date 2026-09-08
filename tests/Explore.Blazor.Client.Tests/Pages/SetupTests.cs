using Explore.Blazor.Client.Models.Responses;
using Explore.Blazor.Client.Pages;

namespace Explore.Blazor.Client.Tests.Pages;

public class SetupTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private readonly IInstanceOnboardingService _instanceOnboardingService;

    public SetupTests()
    {
        _ctx = new BlazorTestContext();
        _instanceOnboardingService = Substitute.For<IInstanceOnboardingService>();
        _ctx.Services.AddSingleton(_instanceOnboardingService);
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    /// <summary>
    /// Sets up JS interop for the /js/bff.js module used by Setup.razor.
    /// All BFF calls go through browser fetch via JS interop.
    /// </summary>
    private void SetupBffJsModule(
        bool hasPersistedSecret = false,
        bool isValid = false,
        string? error = null,
        bool persistOk = true,
        bool syncOk = true,
        bool includeProviders = true)
    {
        var module = _ctx.JSInterop.SetupModule("/js/bff.js");

        module.Setup<SetupSecretStatusResponse>("getSetupSecretStatus")
            .SetResult(new SetupSecretStatusResponse
            {
                HasPersistedSecret = hasPersistedSecret,
                IsValid = isValid,
                Error = error
            });

        module.Setup<BffMutationResult>("persistSetupSecret", _ => true)
            .SetResult(new BffMutationResult
            {
                Ok = persistOk,
                Status = persistOk ? 200 : 400,
                Error = persistOk ? null : "Persist failed."
            });

        module.Setup<BffMutationResult>("syncSetupSecret", _ => true)
            .SetResult(new BffMutationResult
            {
                Ok = syncOk,
                Status = syncOk ? 200 : 400,
                Error = syncOk ? null : "Sync failed."
            });

        module.Setup<BffMutationResult>("deleteSetupSecret")
            .SetResult(new BffMutationResult { Ok = true, Status = 200 });

        if (includeProviders)
        {
            module.Setup<AuthProvidersResponse>("fetchJson", invocation =>
                    invocation.Arguments.Count > 0 &&
                    invocation.Arguments[0]?.ToString()?.Contains("/auth/providers") == true)
                .SetResult(new AuthProvidersResponse
                {
                    Providers =
                    [
                        new() { Name = "Keycloak", DisplayName = "Keycloak", Type = "button" },
                        new() { Name = "Google", DisplayName = "Google", Type = "button" },
                        new() { Name = "Atproto", DisplayName = "AT Protocol", Type = "handle_input" }
                    ]
                });
        }
    }

    [Test]
    public async Task LocalCookiePersistenceForcesFreshAuthorityBeforeContinuing()
    {
        _instanceOnboardingService.GetStatusAsync().Returns(new InstanceOnboardingStatusDto
        {
            IsCompleted = false, Provider = "Local", IsAuthenticated = false
        });
        _instanceOnboardingService.ValidateSecretAsync(Arg.Any<string>()).Returns(new SetupSecretValidationResultDto { Valid = true });
        SetupBffJsModule();
        var navigation = _ctx.Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        navigation.NavigateTo("/setup");
        var cut = _ctx.Render<Setup>();
        cut.Find("input[type=password]").Input(Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
        await cut.Find("form").SubmitAsync();
        await Assert.That(navigation.History.First().Options.ForceLoad).IsTrue();
        await Assert.That(navigation.Uri).EndsWith("/setup");
        await Assert.That(cut.FindAll("input[type=password]").All(input => string.IsNullOrEmpty(input.GetAttribute("value")))).IsTrue();
    }

    [Test]
    public async Task LocalSetupAuthorityOffersSetupRatherThanOrdinaryLogin()
    {
        var status = new InstanceOnboardingStatusDto { IsCompleted = false, Provider = "Local", IsAuthenticated = true };
        status.AdditionalProperties["_links"] = System.Text.Json.JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["complete-local"] = new { href = "/api/instanceonboarding/complete-local", method = "POST" }
        });
        _instanceOnboardingService.GetStatusAsync().Returns(status);
        _instanceOnboardingService.ShouldSkipAuthorizationProviderStepAsync().Returns(true);
        SetupBffJsModule(hasPersistedSecret: true, isValid: true);
        var navigation = _ctx.Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        var cut = _ctx.Render<Setup>();
        await cut.FindAll("button").Single(button => button.TextContent.Contains("Continue Local setup", StringComparison.Ordinal))
            .ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        await Assert.That(navigation.Uri).EndsWith("/onboarding/instance");
        await Assert.That(navigation.History.First().Options.ForceLoad).IsTrue();
        await Assert.That(cut.FindAll("a[href^='/login']")).IsEmpty();
    }

    [Test]
    public async Task Setup_WhenSecretIsRequired_RendersOriginalSecretEntryGateway()
    {
        _instanceOnboardingService.GetStatusAsync().Returns(new InstanceOnboardingStatusDto
        {
            IsCompleted = false,
            IsAuthenticated = false
        });
        SetupBffJsModule();

        var cut = _ctx.Render<Setup>();

        cut.WaitForAssertion(() =>
        {
            var headings = cut.FindAll("h1");
            if (headings.Count != 1
                || !headings[0].TextContent.Contains("Setup Access", StringComparison.OrdinalIgnoreCase)
                || cut.Find("input").GetAttribute("type") != "password"
                || !cut.Markup.Contains("Setup Secret", StringComparison.OrdinalIgnoreCase)
                || !cut.Markup.Contains("Validate Secret", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Expected the original setup-secret entry gateway.");
            }
        });

        await _instanceOnboardingService.Received(1).GetStatusAsync();
    }

    [Test]
    public async Task RestorePersistedSecret_WhenStoredSecretIsInvalid_ShowsError()
    {
        // Arrange
        _instanceOnboardingService.GetStatusAsync().Returns(new InstanceOnboardingStatusDto
        {
            IsCompleted = false,
            IsAuthenticated = true
        });
        SetupBffJsModule(hasPersistedSecret: true, isValid: false, error: "Invalid setup secret.");

        // Act
        var cut = _ctx.Render<Setup>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Invalid setup secret.", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Expected stale setup secret warning was not rendered.");
            }
        });
    }

    [Test]
    public async Task RestorePersistedSecret_WhenStoredSecretIsValid_KeepsValidatedSessionState()
    {
        // Arrange
        _instanceOnboardingService.GetStatusAsync().Returns(new InstanceOnboardingStatusDto
        {
            IsCompleted = false,
            IsAuthenticated = true
        });
        SetupBffJsModule(hasPersistedSecret: true, isValid: true);

        // Act
        var cut = _ctx.Render<Setup>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Setup secret is validated and your session is authenticated.", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Expected validated setup secret state was not rendered.");
            }
        });
    }

    [Test]
    public async Task RestorePersistedSecret_WhenProvidersDetected_ShowsQuickActionsAndConfigureLast()
    {
        // Arrange
        _instanceOnboardingService.GetStatusAsync().Returns(new InstanceOnboardingStatusDto
        {
            IsCompleted = false,
            IsAuthenticated = false
        });
        SetupBffJsModule(hasPersistedSecret: true, isValid: true);

        // Act
        var cut = _ctx.Render<Setup>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Continue with Keycloak", StringComparison.OrdinalIgnoreCase)
                || !cut.Markup.Contains("Continue with Google", StringComparison.OrdinalIgnoreCase)
                || !cut.Markup.Contains("Configure Authentication", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Expected setup quick action buttons were not rendered.");
            }

            var keycloakIndex = cut.Markup.IndexOf("Continue with Keycloak", StringComparison.OrdinalIgnoreCase);
            var googleIndex = cut.Markup.IndexOf("Continue with Google", StringComparison.OrdinalIgnoreCase);
            var configureIndex = cut.Markup.IndexOf("Configure Authentication", StringComparison.OrdinalIgnoreCase);

            if (configureIndex < keycloakIndex || configureIndex < googleIndex)
            {
                throw new InvalidOperationException("Configure Authentication button should be rendered after provider quick actions.");
            }
        });
    }

    [Test]
    [Arguments(false, "/onboarding/authz-provider")]
    [Arguments(true, "/onboarding/instance")]
    public async Task Setup_WhenKeycloakQuickActionClicked_UsesAuthoritativeAuthorizationDestination(
        bool skipAuthorizationProvider,
        string returnUrl)
    {
        // Arrange
        _instanceOnboardingService.GetStatusAsync().Returns(new InstanceOnboardingStatusDto
        {
            IsCompleted = false,
            IsAuthenticated = false
        });
        _instanceOnboardingService.ShouldSkipAuthorizationProviderStepAsync().Returns(skipAuthorizationProvider);
        SetupBffJsModule(hasPersistedSecret: true, isValid: true);

        var nav = _ctx.Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        nav.NavigateTo("/setup");

        // Act
        var cut = _ctx.Render<Setup>();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Continue with Keycloak", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Expected Keycloak quick action button was not rendered.");
            }
        });

        var keycloakButton = cut
            .FindAll("button")
            .First(button => button.TextContent.Contains("Continue with Keycloak", StringComparison.OrdinalIgnoreCase));

        keycloakButton.Click();

        cut.WaitForAssertion(() =>
        {
            if (!nav.Uri.EndsWith(
                    $"/login?provider=keycloak&returnUrl={Uri.EscapeDataString(returnUrl)}",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected login return URL: '{nav.Uri}'.");
            }
        });

        await _instanceOnboardingService.Received(1).ShouldSkipAuthorizationProviderStepAsync();
    }

    [Test]
    public void Setup_WhenAuthenticationConfigurationSelected_EntersAuthProviderOnboarding()
    {
        _instanceOnboardingService.GetStatusAsync().Returns(new InstanceOnboardingStatusDto
        {
            IsCompleted = false,
            IsAuthenticated = false
        });
        SetupBffJsModule(hasPersistedSecret: true, isValid: true, includeProviders: false);
        var nav = _ctx.Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        nav.NavigateTo("/setup");

        var cut = _ctx.Render<Setup>();

        cut.WaitForAssertion(() => cut.FindAll("button")
            .First(button => button.TextContent.Contains("Configure Authentication", StringComparison.OrdinalIgnoreCase))
            .Click());

        cut.WaitForAssertion(() =>
        {
            if (!nav.Uri.EndsWith("/onboarding/auth-provider", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected authentication configuration URL: '{nav.Uri}'.");
            }
        });
    }

    [Test]
    public async Task Setup_WhenEnvironmentSecretActive_ShowsEnvironmentGuidance()
    {
        // Arrange
        _instanceOnboardingService.GetStatusAsync().Returns(new InstanceOnboardingStatusDto
        {
            IsCompleted = false,
            IsAuthenticated = false,
            IsSetupModeActive = true,
            SetupSecretFromEnvironment = true,
            SetupSecretState = "Environment",
            SetupSecretGuidance = "Use the SETUP_SECRET environment variable configured for this deployment."
        });
        SetupBffJsModule();

        // Act
        var cut = _ctx.Render<Setup>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Use the SETUP_SECRET environment variable", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Expected environment setup-secret guidance was not rendered.");
            }

            if (cut.Markup.Contains("Setup window", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Boot-relative setup countdown must not be rendered.");
            }
        });
    }

    [Test]
    public async Task Setup_WhenGeneratedSecretActive_ShowsDockerHostRetrievalGuidance()
    {
        // Arrange
        _instanceOnboardingService.GetStatusAsync().Returns(new InstanceOnboardingStatusDto
        {
            IsCompleted = false,
            IsAuthenticated = false,
            IsSetupModeActive = true,
            SetupSecretState = "Generated",
            SetupSecretGuidance = "Retrieve the generated secret using the Docker-host instruction in the application logs."
        });
        SetupBffJsModule();

        // Act
        var cut = _ctx.Render<Setup>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Docker-host", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Expected generated setup-secret recovery guidance was not rendered.");
            }
        });
    }

    [Test]
    public void Setup_WithReturnUrl_ContinuesToSafeOnboardingLocation()
    {
        _instanceOnboardingService.GetStatusAsync().Returns(new InstanceOnboardingStatusDto
        {
            IsCompleted = false,
            IsAuthenticated = true
        });
        SetupBffJsModule(hasPersistedSecret: true, isValid: true);
        var nav = _ctx.Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        nav.NavigateTo("/setup?returnUrl=%2Fonboarding%2Finstance%3Fsection%3Dlaunch");
        var cut = _ctx.Render<Setup>();
        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Continue Setup", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Expected setup continuation action was not rendered.");
            }
        });

        cut.FindAll("button")
            .First(button => button.TextContent.Contains("Continue Setup", StringComparison.OrdinalIgnoreCase))
            .Click();

        cut.WaitForAssertion(() =>
        {
            if (!nav.Uri.EndsWith("/onboarding/instance?section=launch", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected setup resume URL: '{nav.Uri}'.");
            }
        });

    }

    [Test]
    public void Setup_AfterValidation_ResumesSafeReturnUrl()
    {
        _instanceOnboardingService.GetStatusAsync().Returns(new InstanceOnboardingStatusDto
        {
            IsCompleted = false,
            IsAuthenticated = true
        });
        _instanceOnboardingService.ValidateSecretAsync("candidate-secret")
            .Returns(new SetupSecretValidationResultDto { Valid = true });
        SetupBffJsModule();
        var nav = _ctx.Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        nav.NavigateTo("/setup?returnUrl=%2Fonboarding%2Finstance%3Fsection%3Dlaunch");
        var cut = _ctx.Render<Setup>();
        cut.WaitForAssertion(() => cut.Find("form"));

        cut.Find("input").Input("candidate-secret");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            if (!nav.Uri.EndsWith("/onboarding/instance?section=launch", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected setup resume URL: '{nav.Uri}'.");
            }
        });
    }

    [Test]
    public void Setup_WithExternalReturnUrl_UsesAuthoritativeOnboardingDestination()
    {
        _instanceOnboardingService.GetStatusAsync().Returns(new InstanceOnboardingStatusDto
        {
            IsCompleted = false,
            IsAuthenticated = true
        });
        _instanceOnboardingService.ShouldSkipAuthorizationProviderStepAsync().Returns(true);
        SetupBffJsModule(hasPersistedSecret: true, isValid: true);
        var nav = _ctx.Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        nav.NavigateTo("/setup?returnUrl=https%3A%2F%2Fevil.example");
        var cut = _ctx.Render<Setup>();
        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Continue Setup", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Expected setup continuation action was not rendered.");
            }
        });

        cut.FindAll("button")
            .First(button => button.TextContent.Contains("Continue Setup", StringComparison.OrdinalIgnoreCase))
            .Click();

        cut.WaitForAssertion(() =>
        {
            if (!nav.Uri.EndsWith("/onboarding/instance", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected fallback URL: '{nav.Uri}'.");
            }
        });
    }

    [Test]
    public async Task Setup_WhenCompleted_RedirectsToHome()
    {
        // Arrange
        _instanceOnboardingService.GetStatusAsync().Returns(new InstanceOnboardingStatusDto
        {
            IsCompleted = true,
            IsAuthenticated = true
        });
        SetupBffJsModule();

        var nav = _ctx.Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();

        // Act
        var cut = _ctx.Render<Setup>();

        // Assert
        await Assert.That(nav.Uri).EndsWith("/");
    }
}
