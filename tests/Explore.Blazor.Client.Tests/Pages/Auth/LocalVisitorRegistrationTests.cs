using System.Security.Cryptography;
using AngleSharp.Html.Dom;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Models.Requests;
using Explore.Blazor.Client.Pages.Auth;
using Explore.Blazor.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Refit;

namespace Explore.Blazor.Client.Tests.Pages.Auth;

public sealed class LocalVisitorRegistrationTests
{
    [Test]
    public async Task LocalProviderWithoutHalCannotExposeTheCredentialForm()
    {
        using var context = CreateContext(allowed: false);
        var cut = context.Render<LocalVisitorRegistration>();
        await Assert.That(cut.FindAll("form, input[type=password]").Count).IsEqualTo(0);
        await Assert.That(cut.Find("[role=status]").TextContent).Contains("not available");
    }

    [Test]
    public async Task UnavailableDiscoveryDoesNotClaimEnrollmentIsClosed()
    {
        using var context = CreateContext(healthy: false);
        var cut = context.Render<LocalVisitorRegistration>();
        await Assert.That(cut.Find("[role=alert]").TextContent).Contains("could not be checked");
        await Assert.That(cut.FindAll("form").Count).IsEqualTo(0);
    }

    [Test]
    public async Task CallableHalRendersLabeledCredentialsAndOptionalUnverifiedEmail()
    {
        using var context = CreateContext();
        var module = context.JSInterop.SetupModule("./js/bff.js");
        var cut = context.Render<LocalVisitorRegistration>();
        await Assert.That(cut.FindAll("h1").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("input[type=password]").Count).IsEqualTo(2);
        await Assert.That(cut.FindAll("input[type=email]").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("input[type=hidden]").Count).IsEqualTo(0);
        foreach (var label in cut.FindAll("label"))
            await Assert.That(cut.FindAll("input").Any(input => input.Id == label.GetAttribute("for"))).IsTrue();
        await Assert.That(cut.Find("input[type=email]").HasAttribute("required")).IsFalse();
        await Assert.That(cut.Markup).Contains("Email remains unverified");
        await Assert.That(module.Invocations.Any(invocation =>
            invocation.Identifier is "startLocalVisitorEnrollment" or "registerLocalVisitorEnrollment")).IsFalse();
    }

    [Test]
    public async Task PasswordMismatchNeverBeginsAnEnrollmentFlow()
    {
        using var context = CreateContext();
        var module = context.JSInterop.SetupModule("./js/bff.js");
        var cut = context.Render<LocalVisitorRegistration>();
        Fill(cut, Password(), Password());
        cut.Find("form").Submit();
        await Assert.That(cut.Find("[role=alert]").TextContent).Contains("do not match");
        await Assert.That(module.Invocations.Any(invocation =>
            invocation.Identifier is "startLocalVisitorEnrollment" or "registerLocalVisitorEnrollment")).IsFalse();
    }

    [Test]
    public async Task ConfirmedAccountOffersOnlyTheSafeExistingLoginContinuation()
    {
        using var context = CreateContext();
        var module = context.JSInterop.SetupModule("./js/bff.js");
        module.Setup<LocalVisitorEnrollmentBrowserResult>("startLocalVisitorEnrollment", _ => true)
            .SetResult(new(true, 200, "ready", null));
        module.Setup<LocalVisitorEnrollmentBrowserResult>("registerLocalVisitorEnrollment", _ => true)
            .SetResult(new(true, 200, "account_created", "/login?returnUrl=%2F"));
        var cut = context.Render<LocalVisitorRegistration>();
        string password = Password();
        Fill(cut, password, password);
        cut.Find("form").Submit();
        cut.WaitForState(() => cut.FindAll("a[href='/login?returnUrl=%2F']")
            .Any(link => link.TextContent.Contains("Sign in", StringComparison.Ordinal)));
        await Assert.That(cut.FindAll("form, input[type=password]").Count).IsEqualTo(0);
        var request = (LocalVisitorEnrollmentSubmission)module.Invocations
            .Single(invocation => invocation.Identifier == "registerLocalVisitorEnrollment").Arguments[0]!;
        await Assert.That(request.Username).IsEqualTo("visitor");
        await Assert.That(request.Password).IsEqualTo(password);
        await Assert.That(request.Email).IsNull();
        await Assert.That(request.ToString().Contains(password, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task UnconfirmedOutcomeClearsCredentialsAndKeepsExplicitReconciliationAvailable()
    {
        using var context = CreateContext();
        var module = context.JSInterop.SetupModule("./js/bff.js");
        module.Setup<LocalVisitorEnrollmentBrowserResult>("startLocalVisitorEnrollment", _ => true)
            .SetResult(new(true, 200, "ready", null));
        module.Setup<LocalVisitorEnrollmentBrowserResult>("registerLocalVisitorEnrollment", _ => true)
            .SetResult(new(false, 503, null, null));
        var cut = context.Render<LocalVisitorRegistration>();
        string password = Password();
        Fill(cut, password, password);
        cut.Find("form").Submit();
        cut.WaitForState(() => cut.FindAll("[role=alert]")
            .Any(message => message.TextContent.Contains("same original credentials", StringComparison.Ordinal)));
        foreach (IHtmlInputElement input in cut.FindAll("input[type=password]"))
            await Assert.That(input.Value).IsEqualTo(string.Empty);
        await Assert.That(cut.FindAll("form").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("a").Count).IsEqualTo(0);
    }

    [Test]
    public async Task UnexpectedExternalLoginContinuationCannotBecomeANavigationAction()
    {
        using var context = CreateContext();
        var module = context.JSInterop.SetupModule("./js/bff.js");
        module.Setup<LocalVisitorEnrollmentBrowserResult>("startLocalVisitorEnrollment", _ => true)
            .SetResult(new(true, 200, "ready", null));
        module.Setup<LocalVisitorEnrollmentBrowserResult>("registerLocalVisitorEnrollment", _ => true)
            .SetResult(new(true, 200, "account_created", "https://other.example.test/login"));
        var cut = context.Render<LocalVisitorRegistration>();
        string password = Password();
        Fill(cut, password, password);
        cut.Find("form").Submit();
        cut.WaitForState(() => cut.FindAll("[role=alert]")
            .Any(message => message.TextContent.Contains("not confirmed", StringComparison.Ordinal)));
        await Assert.That(cut.FindAll("a").Count).IsEqualTo(0);
    }

    private static BlazorTestContext CreateContext(bool allowed = true, bool healthy = true)
    {
        var context = new BlazorTestContext();
        var response = Substitute.For<IApiResponse<BffAuthProvidersResponse>>();
        response.IsSuccessStatusCode.Returns(healthy);
        response.Content.Returns(new BffAuthProvidersResponse
        {
            PrimaryProvider = "local",
            _links = allowed ? new Dictionary<string, HalLink> { ["local-register"] = new() } : []
        });
        var api = Substitute.For<IBffAuthApi>();
        api.GetProvidersAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(response));
        context.Services.AddSingleton(api);
        return context;
    }

    private static void Fill(IRenderedComponent<LocalVisitorRegistration> cut, string password, string confirmation)
    {
        cut.FindAll("input").First(input => input.GetAttribute("type") is null or "text").Input("visitor");
        cut.FindAll("input[type=password]")[0].Input(password);
        cut.FindAll("input[type=password]")[1].Input(confirmation);
    }

    private static string Password() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
