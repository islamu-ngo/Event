// ABOUTME: Verifies accessible first-use Local password replacement through the browser-to-BFF boundary.
// ABOUTME: Guards credential clearing, fixed login navigation, and value-free request diagnostics.

using System.Security.Cryptography;
using System.Text.Json;
using AngleSharp.Html.Dom;
using Explore.Blazor.Client.Models.Requests;
using Explore.Blazor.Client.Pages.Auth;

namespace Explore.Blazor.Client.Tests.Pages.Auth;

public sealed class LocalPasswordChangeTests
{
    [Test]
    public async Task ReplacementFormRendersLabeledPasswordsAndConnectedHelp()
    {
        using var context = new BlazorTestContext();
        var cut = context.Render<LocalPasswordChange>();

        await Assert.That(cut.FindAll("h1").Count).IsEqualTo(1);
        await Assert.That(cut.Find("h1").TextContent).Contains("Change your password");
        await Assert.That(cut.FindAll("input[type=password]").Count).IsEqualTo(2);
        await Assert.That(cut.FindAll("input[type=hidden], input[type=email]").Count).IsEqualTo(0);
        await Assert.That(cut.Find("#local-password-change-form button[type=submit]").TextContent)
            .Contains("Change password");
        await Assert.That(cut.Find("#local-password-help").TextContent).Contains("12");
        await Assert.That(cut.Find("#local-password-help").TextContent).Contains("128");

        foreach (var field in new[]
        {
            (Id: "local-new-password", Label: "New password"),
            (Id: "local-confirm-password", Label: "Confirm new password")
        })
        {
            var input = cut.Find($"#{field.Id}");
            await Assert.That(cut.Find($"label[for='{field.Id}']").TextContent).IsEqualTo(field.Label);
            await Assert.That(input.GetAttribute("type")).IsEqualTo("password");
            await Assert.That(input.GetAttribute("autocomplete")).IsEqualTo("new-password");
            await Assert.That(input.GetAttribute("aria-describedby")?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("local-password-help") == true).IsTrue();
        }
    }

    [Test]
    public async Task SuccessfulNativeFormSubmissionSendsOnlyNewPasswordAndHardNavigatesToLogin()
    {
        using var context = new BlazorTestContext();
        var password = CreatePassword();
        LocalBffCredentialReplacementRequest? captured = null;
        var argumentCount = 0;
        context.JSInterop.SetupModule("/js/bff.js")
            .Setup<LocalBffAuthenticationResponse?>("replaceLocalCredential", invocation =>
            {
                captured = invocation.Arguments.OfType<LocalBffCredentialReplacementRequest>().SingleOrDefault();
                argumentCount = invocation.Arguments.Count;
                return true;
            })
            .SetResult(new LocalBffAuthenticationResponse(RedirectUrl: "/login"));
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/auth/local/change-password?returnUrl=%2Fdashboard");
        var cut = context.Render<LocalPasswordChange>();

        cut.Find("#local-new-password").Change(password);
        cut.Find("#local-confirm-password").Change(password);
        cut.Find("#local-password-change-form").Submit();

        await Assert.That(captured is not null && captured.NewPassword == password).IsTrue();
        await Assert.That(argumentCount).IsEqualTo(1);
        var requestFields = JsonSerializer.SerializeToElement(captured).EnumerateObject()
            .Select(property => property.Name).ToArray();
        await Assert.That(requestFields.Length == 1 && requestFields[0] == "NewPassword").IsTrue();
        await Assert.That(navigation.Uri).IsEqualTo("http://localhost/login");
        await Assert.That(navigation.History.First().Options.ForceLoad).IsTrue();
        await Assert.That(((IHtmlInputElement)cut.Find("#local-new-password")).Value.Length).IsEqualTo(0);
        await Assert.That(((IHtmlInputElement)cut.Find("#local-confirm-password")).Value.Length).IsEqualTo(0);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task RejectedReplacementClearsPasswordsAndConnectsOneSafeError(bool hasProviderError)
    {
        using var context = new BlazorTestContext();
        var password = CreatePassword();
        var providerDetail = Guid.NewGuid().ToString("N");
        context.JSInterop.SetupModule("/js/bff.js")
            .Setup<LocalBffAuthenticationResponse?>("replaceLocalCredential", _ => true)
            .SetResult(new LocalBffAuthenticationResponse(
                RedirectUrl: "/dashboard",
                ErrorCode: hasProviderError ? providerDetail : null));
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/auth/local/change-password");
        var cut = context.Render<LocalPasswordChange>();

        cut.Find("#local-new-password").Change(password);
        cut.Find("#local-confirm-password").Change(password);
        cut.Find("#local-password-change-form").Submit();

        await Assert.That(navigation.Uri).EndsWith("/auth/local/change-password");
        await Assert.That(cut.FindAll("[role=alert]").Count).IsEqualTo(1);
        await Assert.That(cut.Find("[role=alert]").Id).IsEqualTo("local-password-error");
        await Assert.That(cut.Find("[role=alert]").HasAttribute("hidden")).IsFalse();
        await Assert.That(string.IsNullOrWhiteSpace(cut.Find("#local-password-error").TextContent)).IsFalse();
        await Assert.That(cut.Markup.Contains(password, StringComparison.Ordinal)
            || cut.Markup.Contains(providerDetail, StringComparison.Ordinal)).IsFalse();

        foreach (var input in cut.FindAll("input[type=password]").Cast<IHtmlInputElement>())
        {
            await Assert.That(input.Value.Length).IsEqualTo(0);
            await Assert.That(input.GetAttribute("aria-invalid")).IsEqualTo("true");
            var descriptionIds = input.GetAttribute("aria-describedby")?
                .Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
            await Assert.That(descriptionIds.Contains("local-password-help")).IsTrue();
            await Assert.That(descriptionIds.Contains("local-password-error")).IsTrue();
            foreach (var descriptionId in descriptionIds)
            {
                await Assert.That(cut.FindAll($"[id='{descriptionId}']").Count).IsEqualTo(1);
            }
        }
    }

    [Test]
    public async Task CredentialRequestDiagnosticsNeverIncludeCredentials()
    {
        var password = CreatePassword();
        var email = $"{Guid.NewGuid():N}@example.test";
        var login = new LocalBffLoginRequest(
            Identifier: email,
            Password: password,
            IsPersistent: false,
            ReturnUrl: "/");
        var replacement = new LocalBffCredentialReplacementRequest(NewPassword: password);

        await Assert.That(login.ToString().Contains(email, StringComparison.Ordinal)
            || login.ToString().Contains(password, StringComparison.Ordinal)
            || replacement.ToString().Contains(password, StringComparison.Ordinal)).IsFalse();
    }

    private static string CreatePassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
