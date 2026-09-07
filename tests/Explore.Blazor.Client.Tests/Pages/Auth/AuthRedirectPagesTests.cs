using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Bunit.TestDoubles;
using Explore.Blazor.Client.Pages.Auth;
using Explore.Blazor.Client.Services.Http;
using Explore.Blazor.Client.Tests.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Refit;

namespace Explore.Blazor.Client.Tests.Pages.Auth;

public class AuthRedirectPagesTests : IDisposable
{
    private readonly BlazorTestContext _ctx;

    public AuthRedirectPagesTests()
    {
        _ctx = new BlazorTestContext();
        _ctx.Services.AddSingleton(Substitute.For<IBffClient>());
        ConfigureAuthProviderClient(new
        {
            providers = new[]
            {
                new { name = "Keycloak", displayName = "Keycloak", type = "button", recommended = true }
            }
        });
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    [Test]
    public async Task LoginRedirect_NavigatesToAuthChallenge_WhenNoQueryString()
    {
        // Arrange
        var nav = _ctx.Services.GetRequiredService<BunitNavigationManager>();
        nav.NavigateTo("/login");

        // Act
        var cut = _ctx.Render<LoginRedirect>();

        // Assert
        await WaitForNavigationAsync(nav, uri => IsChallengeNavigation(uri, "keycloak", "/"));

        await AssertChallengeNavigationAsync(nav.Uri, "keycloak", "/");
    }

    [Test]
    public async Task LoginRedirect_ForwardsQueryString_ToAuthChallenge()
    {
        // Arrange
        var nav = _ctx.Services.GetRequiredService<BunitNavigationManager>();
        nav.NavigateTo("/login?returnUrl=%2Fsettings%2Fadmin");

        // Act
        var cut = _ctx.Render<LoginRedirect>();

        // Assert
        await WaitForNavigationAsync(nav, uri => IsChallengeNavigation(uri, "keycloak", "/settings/admin"));

        await AssertChallengeNavigationAsync(nav.Uri, "keycloak", "/settings/admin");
    }

    [Test]
    [Arguments("https%3A%2F%2Fevil.example%2Fcallback")]
    [Arguments("%2F%2Fevil.example%2Fcallback")]
    [Arguments("%2F%5Cevil.example%2Fcallback")]
    public async Task LoginRedirect_WithUnsafeReturnUrl_FallsBackToLocalRoot(string unsafeReturnUrl)
    {
        var nav = _ctx.Services.GetRequiredService<BunitNavigationManager>();
        nav.NavigateTo($"/login?returnUrl={unsafeReturnUrl}");

        _ctx.Render<LoginRedirect>();

        await WaitForNavigationAsync(nav, uri => IsChallengeNavigation(uri, "keycloak", "/"));
        await AssertChallengeNavigationAsync(nav.Uri, "keycloak", "/");
        await Assert.That(nav.Uri).DoesNotContain("evil.example");
    }

    [Test]
    public async Task LoginRedirect_WithMultipleProviders_ShouldNotAutoRedirect()
    {
        // Arrange
        ConfigureAuthProviderClient(new
        {
            providers = new[]
            {
                new { name = "Keycloak", displayName = "Keycloak", type = "button", recommended = true },
                new { name = "Google", displayName = "Google", type = "button", recommended = false },
                new { name = "Atproto", displayName = "AT Protocol", type = "handle_input", recommended = false }
            }
        });

        var nav = _ctx.Services.GetRequiredService<BunitNavigationManager>();
        nav.NavigateTo("/login?returnUrl=%2Fevents");

        // Act
        var cut = _ctx.Render<LoginRedirect>();

        // Assert
        await Assert.That(nav.Uri).EndsWith("/login?returnUrl=%2Fevents");
        await Assert.That(cut.Markup).Contains("Continue with Keycloak");
        await Assert.That(cut.Markup).Contains("Continue with Google");
        await Assert.That(cut.Markup).Contains("Continue with ATProto");
        await Assert.That(cut.Markup.IndexOf("Continue with Keycloak", StringComparison.Ordinal))
            .IsLessThan(cut.Markup.IndexOf("Continue with Google", StringComparison.Ordinal));
        await Assert.That(cut.Markup.IndexOf("Continue with Google", StringComparison.Ordinal))
            .IsLessThan(cut.Markup.IndexOf("Continue with ATProto", StringComparison.Ordinal));
    }

    [Test]
    public async Task LoginRedirect_WithForcedProvider_ShouldAutoRedirectToThatProvider()
    {
        // Arrange
        ConfigureAuthProviderClient(new
        {
            providers = new[]
            {
                new { name = "Keycloak", displayName = "Keycloak", type = "button", recommended = true },
                new { name = "Google", displayName = "Google", type = "button", recommended = false }
            }
        });

        var nav = _ctx.Services.GetRequiredService<BunitNavigationManager>();
        nav.NavigateTo("/login?provider=google&returnUrl=%2Fsetup");

        // Act
        var cut = _ctx.Render<LoginRedirect>();

        // Assert
        await WaitForNavigationAsync(nav, uri => IsChallengeNavigation(uri, "google", "/setup"));

        await AssertChallengeNavigationAsync(nav.Uri, "google", "/setup");
    }

    [Test]
    public async Task LoginRedirect_WithChallengeError_ShouldNotAutoRedirect()
    {
        // Arrange — single provider + forced provider + challengeError=1
        // This scenario occurs when OIDC callback fails (OnRemoteFailure).
        // The login page must NOT auto-redirect back to the same provider, or it loops.
        var nav = _ctx.Services.GetRequiredService<BunitNavigationManager>();
        nav.NavigateTo("/login?returnUrl=%2Fsetup&challengeError=1&provider=keycloak");

        // Act
        var cut = _ctx.Render<LoginRedirect>();

        // Assert — page stays on /login, shows error message, does NOT redirect
        await Assert.That(nav.Uri).EndsWith("/login?returnUrl=%2Fsetup&challengeError=1&provider=keycloak");
        await Assert.That(cut.Markup).Contains("The last login attempt failed");
        await Assert.That(cut.Markup).Contains("Continue with Keycloak");
    }

    [Test]
    public async Task LoginRedirect_WithChallengeErrorAndErrorDetail_DoesNotRenderErrorDetail()
    {
        var nav = _ctx.Services.GetRequiredService<BunitNavigationManager>();
        nav.NavigateTo("/login?returnUrl=%2Fsetup&challengeError=1&provider=keycloak&errorDetail=secretLen%3D24%7CclientId%3Dislamu-event-blazor");

        var cut = _ctx.Render<LoginRedirect>();

        await Assert.That(cut.Markup).Contains("The last login attempt failed");
        await Assert.That(cut.Markup).Contains("Continue with Keycloak");
        await Assert.That(cut.Markup).DoesNotContain("secretLen");
        await Assert.That(cut.Markup).DoesNotContain("clientId");
        await Assert.That(cut.Markup).DoesNotContain("islamu-event-blazor");
    }

    [Test]
    public async Task LoginRedirect_WithAtprotoCallbackError_MapsOnlyTheStableCode()
    {
        var nav = _ctx.Services.GetRequiredService<BunitNavigationManager>();
        nav.NavigateTo("/login?challengeError=1&provider=atproto&errorCode=atproto_callback_failed&correlationId=opaque");

        var cut = _ctx.Render<LoginRedirect>();

        var alert = cut.Find("[role=alert]");
        await Assert.That(alert.TextContent).Contains("ATProto sign-in could not be completed");
        await Assert.That(cut.Markup).DoesNotContain("correlationId");
        await Assert.That(cut.Markup).DoesNotContain("opaque");
    }

    [Test]
    public async Task LoginRedirect_WithLoginHint_DoesNotImportOrSubmitTheHandle()
    {
        // Arrange
        ConfigureAuthProviderClient(new
        {
            providers = new[]
            {
                new { name = "Atproto", displayName = "AT Protocol", type = "handle_input", recommended = false }
            }
        });

        const string handleCanary = "browser-secret.bsky.social";
        var nav = _ctx.Services.GetRequiredService<BunitNavigationManager>();
        nav.NavigateTo($"/login?returnUrl=%2Fdashboard&login_hint={handleCanary}");

        // Act
        var cut = _ctx.Render<LoginRedirect>();

        // Assert
        await Assert.That(cut.Markup).Contains("Continue with ATProto");
        await Assert.That(cut.Markup).DoesNotContain(handleCanary);
        await Assert.That(cut.Markup).DoesNotContain("ATProto handle");
    }

    [Test]
    public async Task LoginRedirect_WithSingleAtprotoWithoutLoginHint_ShouldNotAutoRedirect()
    {
        // Arrange
        ConfigureAuthProviderClient(new
        {
            providers = new[]
            {
                new { name = "Atproto", displayName = "AT Protocol", type = "handle_input", recommended = false }
            }
        });

        var nav = _ctx.Services.GetRequiredService<BunitNavigationManager>();
        nav.NavigateTo("/login?returnUrl=%2Fdashboard");

        // Act
        var cut = _ctx.Render<LoginRedirect>();

        // Assert
        await Assert.That(nav.Uri).EndsWith("/login?returnUrl=%2Fdashboard");
        await Assert.That(cut.Markup).Contains("Continue with ATProto");
        await Assert.That(cut.Markup).DoesNotContain("ATProto handle");
    }

    [Test]
    public async Task LoginRedirect_WithAtprotoPrimary_ShowsFocusedHandleFormOnly()
    {
        ConfigureAuthProviderClient(new
        {
            primaryProvider = "atproto",
            atprotoLoginEnabled = true,
            providers = new[]
            {
                new
                {
                    name = "Atproto",
                    displayName = "AT Protocol",
                    type = "handle_input",
                    recommended = true
                }
            }
        });
        var nav =
            _ctx.Services.GetRequiredService<BunitNavigationManager>();
        nav.NavigateTo("/login?returnUrl=%2Fonboarding%2Finstance");

        var cut = _ctx.Render<LoginRedirect>();

        await Assert.That(cut.Markup).Contains("ATProto handle");
        await Assert.That(cut.Markup).Contains(
            "Sign in without a local password");
        await Assert.That(cut.Markup).DoesNotContain(
            "Continue with ATProto");
        await Assert.That(cut.Markup).DoesNotContain("Email address");
        await Assert.That(cut.Markup).DoesNotContain("Password");
    }

    [Test]
    public async Task LoginRedirect_ClickingAtproto_ShouldRevealHandleInput()
    {
        // Arrange
        ConfigureAuthProviderClient(new
        {
            providers = new[]
            {
                new { name = "Keycloak", displayName = "Keycloak", type = "button", recommended = true },
                new { name = "Google", displayName = "Google", type = "button", recommended = false },
                new { name = "Atproto", displayName = "AT Protocol", type = "handle_input", recommended = false }
            }
        });

        var nav = _ctx.Services.GetRequiredService<BunitNavigationManager>();
        nav.NavigateTo("/login?returnUrl=%2Fdashboard");

        var cut = _ctx.Render<LoginRedirect>();

        // Act
        var atprotoButton = cut.FindAll("button")
            .First(button => button.TextContent.Contains("Continue with ATProto", StringComparison.Ordinal));
        atprotoButton.Click();

        // Assert
        await Assert.That(nav.Uri).EndsWith("/login?returnUrl=%2Fdashboard");
        await Assert.That(cut.Markup).Contains("ATProto handle");
    }

    [Test]
    public async Task LoginRedirect_WithWhitespaceHandle_KeepsSubmitDisabledAndDoesNotPost()
    {
        ConfigureAtprotoProvider();
        var bffClient = Substitute.For<IBffClient>();
        _ctx.Services.AddSingleton(bffClient);
        var nav = _ctx.Services.GetRequiredService<BunitNavigationManager>();
        nav.NavigateTo("/login");
        var cut = _ctx.Render<LoginRedirect>();

        cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Continue with ATProto", StringComparison.Ordinal))
            .Click();
        cut.Find("input").Input("   ");

        await Assert.That(cut.Find("button[type=submit]").HasAttribute("disabled")).IsTrue();
        await bffClient.DidNotReceiveWithAnyArgs().SendAsync<object, object>(default!, default!, default!);
    }

    [Test]
    public async Task LoginRedirect_SubmittingAtprotoHandle_PostsItAndKeepsItOutOfTheRedirectUrl()
    {
        ConfigureAtprotoProvider();
        const string handleCanary = "user.bsky.social";
        const string authorizationUrl = "https://issuer.example.test/authorize?request_uri=urn%3Apar%3Aopaque";
        HttpMethod? observedMethod = null;
        string? observedPath = null;
        string? observedBody = null;
        ConfigureBffClient(request =>
        {
            observedMethod = request.Method;
            observedPath = request.RequestUri?.AbsolutePath;
            observedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { authorizationUrl })
            };
        });
        var nav = _ctx.Services.GetRequiredService<BunitNavigationManager>();
        nav.NavigateTo("/login?returnUrl=%2Fdashboard");
        var cut = _ctx.Render<LoginRedirect>();

        cut.FindAll("button")
            .First(button => button.TextContent.Contains("Continue with ATProto", StringComparison.Ordinal))
            .Click();
        var input = cut.Find("input");
        input.Input(handleCanary);
        cut.Find("form").Submit();

        await WaitForNavigationAsync(nav, uri => string.Equals(uri, authorizationUrl, StringComparison.Ordinal));
        await Assert.That(cut.Markup).Contains("ATProto handle");
        await Assert.That(cut.FindComponent<MudTextField<string>>().Instance.AutoFocus).IsTrue();
        await Assert.That(cut.Find("button[type=submit]")).IsNotNull();
        await Assert.That(observedMethod).IsEqualTo(HttpMethod.Post);
        await Assert.That(observedPath).IsEqualTo("/auth/atproto/challenge");
        await Assert.That(observedBody).Contains($"\"handle\":\"{handleCanary}\"");
        await Assert.That(observedBody).Contains("\"returnPath\":\"/dashboard\"");
        await Assert.That(nav.Uri).DoesNotContain(handleCanary);
    }

    [Test]
    public async Task LoginRedirect_WhenAtprotoChallengeFails_AnnouncesOnlySafeGuidance()
    {
        ConfigureAtprotoProvider();
        ConfigureBffClient(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new
            {
                title = "provider-private-detail",
                code = "atproto_challenge_failed"
            })
        });
        var nav = _ctx.Services.GetRequiredService<BunitNavigationManager>();
        nav.NavigateTo("/login");
        var cut = _ctx.Render<LoginRedirect>();

        cut.FindAll("button")
            .First(button => button.TextContent.Contains("Continue with ATProto", StringComparison.Ordinal))
            .Click();
        cut.Find("input").Input("user.bsky.social");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            var alert = cut.Find("[role=alert]");
            if (!alert.TextContent.Contains("ATProto sign-in could not be started", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Safe ATProto challenge guidance was not rendered.");
            }
        });
        await Assert.That(cut.Markup).DoesNotContain("provider-private-detail");
        await Assert.That(cut.Markup).DoesNotContain("atproto_challenge_failed");
    }

    [Test]
    public async Task LogoutRedirect_NavigatesToAuthSignout_WhenNoQueryString()
    {
        // Arrange
        var nav = _ctx.Services.GetRequiredService<BunitNavigationManager>();
        nav.NavigateTo("/logout");

        // Act
        _ctx.Render<LogoutRedirect>();

        // Assert
        await Assert.That(nav.Uri).EndsWith("/auth/signout");
    }

    [Test]
    public async Task LogoutRedirect_ForwardsQueryString_ToAuthSignout()
    {
        // Arrange
        var nav = _ctx.Services.GetRequiredService<BunitNavigationManager>();
        nav.NavigateTo("/logout?returnUrl=%2F");

        // Act
        _ctx.Render<LogoutRedirect>();

        // Assert
        await Assert.That(nav.Uri).EndsWith("/auth/signout?returnUrl=%2F");
    }

    private void ConfigureAuthProviderClient(object responsePayload)
    {
        var handler = new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(responsePayload)
            };

            return response;
        });

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://localhost/")
        };
        _ctx.Services.AddSingleton(RestService.For<IBffAuthApi>(client));
    }

    private void ConfigureAtprotoProvider()
    {
        ConfigureAuthProviderClient(new
        {
            providers = new[]
            {
                new { name = "Atproto", displayName = "AT Protocol", type = "handle_input", recommended = false }
            }
        });
    }

    private void ConfigureBffClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var client = new HttpClient(new StubHttpMessageHandler(responder))
        {
            BaseAddress = new Uri("https://localhost/")
        };
        _ctx.Services.AddSingleton<IBffClient>(new BffClient(client, Substitute.For<Microsoft.JSInterop.IJSRuntime>()));
    }

    private static async Task WaitForNavigationAsync(BunitNavigationManager navigationManager, Func<string, bool> predicate, int timeoutMs = 5000)
    {
        if (predicate(navigationManager.Uri))
        {
            return;
        }

        var navigationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnLocationChanged(object? _, LocationChangedEventArgs args)
        {
            if (predicate(args.Location))
            {
                navigationObserved.TrySetResult();
            }
        }

        navigationManager.LocationChanged += OnLocationChanged;
        try
        {
            if (predicate(navigationManager.Uri))
            {
                return;
            }

            await navigationObserved.Task.WaitAsync(
                TimeSpan.FromMilliseconds(timeoutMs));
        }
        finally
        {
            navigationManager.LocationChanged -= OnLocationChanged;
        }
    }

    private static bool IsChallengeNavigation(string uri, string provider, string returnUrl, string? loginHint = null)
    {
        try
        {
            var parsedUri = new Uri(uri, UriKind.Absolute);
            var query = QueryHelpers.ParseQuery(parsedUri.Query);

            if (!string.Equals(parsedUri.AbsolutePath, "/auth/challenge", StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.Equals(query["provider"].ToString(), provider, StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.Equals(query["returnUrl"].ToString(), returnUrl, StringComparison.Ordinal))
            {
                return false;
            }

            if (loginHint is null)
            {
                return !query.ContainsKey("login_hint");
            }

            return string.Equals(query["login_hint"].ToString(), loginHint, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static async Task AssertChallengeNavigationAsync(string uri, string provider, string returnUrl, string? loginHint = null)
    {
        var parsedUri = new Uri(uri, UriKind.Absolute);
        var query = QueryHelpers.ParseQuery(parsedUri.Query);

        await Assert.That(parsedUri.AbsolutePath).IsEqualTo("/auth/challenge");
        await Assert.That(query["provider"].ToString()).IsEqualTo(provider);
        await Assert.That(query["returnUrl"].ToString()).IsEqualTo(returnUrl);

        if (loginHint is null)
        {
            await Assert.That(query.ContainsKey("login_hint")).IsFalse();
        }
        else
        {
            await Assert.That(query["login_hint"].ToString()).IsEqualTo(loginHint);
        }
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }
}
