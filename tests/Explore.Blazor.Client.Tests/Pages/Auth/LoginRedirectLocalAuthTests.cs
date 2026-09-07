using System.Net;
using System.Net.Http.Json;
using Bunit.TestDoubles;
using Explore.Blazor.Client.Models.Requests;
using Explore.Blazor.Client.Pages.Auth;
using Explore.Blazor.Client.Services.Http;
using Explore.Blazor.Client.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using Refit;

namespace Explore.Blazor.Client.Tests.Pages.Auth;

public sealed class LoginRedirectLocalAuthTests
{
    [Test]
    public async Task LocalProviderRendersAccessibleCredentialFields()
    {
        using var context = CreateContext(out _);
        ConfigureLocalProvider(context);
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/login");

        var cut = context.Render<LoginRedirect>();
        var email = cut.Find("input[type=email]");
        var password = cut.Find("input[type=password]");

        await Assert.That(email.GetAttribute("autocomplete")).IsEqualTo("username");
        await Assert.That(password.GetAttribute("autocomplete")).IsEqualTo("current-password");
        await Assert.That(cut.FindAll("label").Any(label =>
            label.TextContent.Contains("Email address", StringComparison.Ordinal))).IsTrue();
        await Assert.That(cut.FindAll("label").Any(label =>
            label.TextContent.Contains("Password", StringComparison.Ordinal))).IsTrue();
        await Assert.That(cut.Find("button[type=submit]").TextContent).Contains("Sign in");
    }

    [Test]
    public async Task ValidLocalCredentialsPostToBffAndHardNavigateToSafeReturnPath()
    {
        using var context = CreateContext(out _);
        ConfigureLocalProvider(context);
        string password = $"{Guid.NewGuid():N}Aa!";
        LocalBffLoginRequest? captured = null;
        var module = context.JSInterop.SetupModule("/js/bff.js");
        module.Setup<LocalBffAuthenticationResponse?>(
                "authenticateLocal",
                invocation =>
            {
                captured =
                    invocation.Arguments[1] as LocalBffLoginRequest;
                return true;
            })
            .SetResult(new LocalBffAuthenticationResponse("/dashboard"));
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/login?returnUrl=%2Fdashboard");
        var cut = context.Render<LoginRedirect>();

        cut.Find("input[type=email]").Change("member@example.test");
        cut.Find("input[type=password]").Change(password);
        cut.Find("form[data-local-login]").Submit();

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.Email).IsEqualTo("member@example.test");
        await Assert.That(captured.Password).IsEqualTo(password);
        await Assert.That(captured.ReturnUrl).IsEqualTo("/dashboard");
        await Assert.That(captured.IsPersistent).IsFalse();
        await Assert.That(navigation.Uri).EndsWith("/dashboard");
    }

    [Test]
    public async Task FailedLocalLoginRendersOnlyStableGuidance()
    {
        using var context = CreateContext(out _);
        ConfigureLocalProvider(context);
        string password = $"{Guid.NewGuid():N}Aa!";
        context.JSInterop.SetupModule("/js/bff.js")
            .Setup<LocalBffAuthenticationResponse?>(
                "authenticateLocal",
                _ => true)
            .SetResult(null);
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/login");
        var cut = context.Render<LoginRedirect>();

        cut.Find("input[type=email]").Change("member@example.test");
        cut.Find("input[type=password]").Change(password);
        cut.Find("form[data-local-login]").Submit();

        var alert = cut.Find("[role=alert]");
        await Assert.That(alert.TextContent).Contains(
            "The email or password was not accepted");
        await Assert.That(cut.Markup).DoesNotContain(password);
        await Assert.That(navigation.Uri).EndsWith("/login");
    }

    [Test]
    public async Task CreateAccountModePostsProfileAndNewPasswordToRegistrationBff()
    {
        using var context = CreateContext(out _);
        ConfigureLocalProvider(context);
        string password = $"{Guid.NewGuid():N}Aa!";
        LocalBffRegistrationRequest? captured = null;
        var module = context.JSInterop.SetupModule("/js/bff.js");
        module.Setup<LocalBffAuthenticationResponse?>(
                "authenticateLocal",
                invocation =>
            {
                captured =
                    invocation.Arguments[1]
                        as LocalBffRegistrationRequest;
                return true;
            })
            .SetResult(new LocalBffAuthenticationResponse("/"));
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/login");
        var cut = context.Render<LoginRedirect>();

        await cut.Find("[data-testid=local-auth-mode-toggle]")
            .ClickAsync(new MouseEventArgs());
        cut.Find("input[autocomplete=given-name]").Change("  Amina  ");
        cut.Find("input[autocomplete=family-name]").Change("  Noor  ");
        cut.Find("input[type=email]").Change("member@example.test");
        cut.Find("input[type=password]").Change(password);
        cut.Find("form").Submit();

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.FirstName).IsEqualTo("Amina");
        await Assert.That(captured.LastName).IsEqualTo("Noor");
        await Assert.That(captured.Email).IsEqualTo("member@example.test");
        await Assert.That(captured.Password).IsEqualTo(password);
        await Assert.That(navigation.Uri).IsEqualTo(navigation.BaseUri);
    }

    private static BlazorTestContext CreateContext(out IBffClient bff)
    {
        var context = new BlazorTestContext();
        bff = Substitute.For<IBffClient>();
        context.Services.AddSingleton(bff);
        return context;
    }

    private static void ConfigureLocalProvider(BlazorTestContext context)
    {
        var providerClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    primaryProvider = "local",
                    atprotoLoginEnabled = false,
                    providers = new[]
                    {
                        new
                        {
                            name = "local",
                            displayName = "Local Identity",
                            type = "credentials",
                            recommended = true
                        }
                    }
                })
            }))
        {
            BaseAddress = new Uri("https://localhost/")
        };
        context.Services.AddSingleton(providerClient);
        context.Services.AddSingleton(RestService.For<IBffAuthApi>(providerClient));
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
