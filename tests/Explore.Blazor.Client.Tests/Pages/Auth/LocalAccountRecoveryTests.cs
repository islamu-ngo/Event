
using System.Security.Cryptography;
using System.Net;
using System.Net.Http.Json;
using AngleSharp.Html.Dom;
using Explore.Blazor.Client.Pages.Auth;
using Explore.Blazor.Client.Services;

namespace Explore.Blazor.Client.Tests.Pages.Auth;

public sealed class LocalAccountRecoveryTests
{
    private static BlazorTestContext CreateContext()
    {
        var context = new BlazorTestContext();
        context.AddMockService<IUserClient>();
        context.Services.AddScoped<IUserService, UserService>();
        return context;
    }

    private static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private static LocalEmailConfirmationRequestDto Pointer(int purpose = 3) => new()
    {
        OperationId = Guid.CreateVersion7(), LocalSubjectId = Guid.CreateVersion7(), PersonalActorId = Guid.CreateVersion7(),
        ExternalLoginId = Guid.CreateVersion7(), Generation = Guid.CreateVersion7(),
        Purpose = purpose, Token = Secret()
    };

    [Test]
    public async Task RecoveryConsumeIsExplicitAndClearsRenderedPasswordsBeforeTheReply()
    {
        using var context = CreateContext();
        var pointer = Pointer();
        var module = context.JSInterop.SetupModule("/js/local-account-recovery.js");
        module.Setup<LocalEmailConfirmationRequestDto?>("takeLocalAccountCapability").SetResult(pointer);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        LocalPasswordRecoveryCompletionRequestDto? captured = null;
        var submission = module.Setup<int>("submitLocalAccountRequest", invocation =>
        {
            captured = invocation.Arguments[1] as LocalPasswordRecoveryCompletionRequestDto;
            entered.TrySetResult();
            return true;
        });
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/auth/local-account-recovery");
        var cut = context.Render<LocalAccountRecovery>();
        await Assert.That(cut.FindAll("h1").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("input[type=password]").Count).IsEqualTo(2);
        await Assert.That(cut.FindAll("input[type=hidden]").Count).IsEqualTo(0);
        await Assert.That(entered.Task.IsCompleted).IsFalse();
        foreach (var field in cut.FindAll("input"))
        {
            await Assert.That(cut.FindAll($"label[for='{field.Id}']").Count).IsEqualTo(1);
            await Assert.That(field.GetAttribute("aria-describedby")).IsEqualTo("local-account-help");
        }
        string password = Secret();
        cut.Find("#local-account-new-password").Change(password);
        cut.Find("#local-account-confirm-password").Change(password);
        var cleansed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cut.OnMarkupUpdated += (_, _) =>
        {
            if (cut.FindAll("input[type=password]").Cast<IHtmlInputElement>().All(field => field.Value.Length == 0)
                && cut.Find("form").GetAttribute("aria-busy") == "true") cleansed.TrySetResult();
        };
        Task action = cut.Find("form").SubmitAsync();
        try
        {
            await Task.WhenAll(entered.Task, cleansed.Task).WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(captured is not null && captured.OperationId == pointer.OperationId
                && captured.Token == pointer.Token && captured.NewPassword == password).IsTrue();
            await Assert.That(cut.FindAll("input").All(field => field.HasAttribute("disabled"))).IsTrue();
            await Assert.That(cut.Markup.Contains(pointer.Token, StringComparison.Ordinal)).IsFalse();
        }
        finally { submission.SetResult(204); }
        await action.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(navigation.Uri).IsEqualTo("http://localhost/login");
        await Assert.That(navigation.History.First().Options.ForceLoad).IsTrue();
    }

    [Test]
    [Arguments(400)]
    [Arguments(409)]
    [Arguments(503)]
    public async Task FailedTokensHaveNoRepeatMutationOrRetainedForm(int status)
    {
        using var context = CreateContext();
        var module = context.JSInterop.SetupModule("/js/local-account-recovery.js");
        module.Setup<LocalEmailConfirmationRequestDto?>("takeLocalAccountCapability").SetResult(Pointer(1));
        module.Setup<int>("submitLocalAccountRequest", _ => true).SetResult(status);
        var cut = context.Render<LocalAccountRecovery>();
        await cut.Find("form").SubmitAsync();
        await Assert.That(cut.FindAll("[role=alert]").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("form, input, button[type=submit]").Count).IsEqualTo(0);
        await Assert.That(module.Invocations["submitLocalAccountRequest"].Count).IsEqualTo(1);
    }

    [Test]
    public async Task AReplyAfterCancellationCannotNavigateOrRestoreCredentials()
    {
        using var context = CreateContext();
        var module = context.JSInterop.SetupModule("/js/local-account-recovery.js");
        module.Setup<LocalEmailConfirmationRequestDto?>("takeLocalAccountCapability").SetResult(Pointer(1));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = module.Setup<int>("submitLocalAccountRequest", _ => { entered.TrySetResult(); return true; });
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/auth/local-account-recovery");
        var cut = context.Render<LocalAccountRecovery>();
        Task action = cut.Find("form").SubmitAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cut.Find("a[href='/login']").ClickAsync(new MouseEventArgs());
        await cut.InvokeAsync(() => navigation.NavigateTo("/settings/personal/security"));
        pending.SetResult(204);
        await action.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(navigation.Uri).IsEqualTo("http://localhost/settings/personal/security");
        await Assert.That(cut.FindAll("form, input").Count).IsEqualTo(0);
    }

    [Test]
    public async Task RoutePurposeMismatchCannotSubmitOrFallBackToDiscovery()
    {
        using var context = CreateContext();
        var module = context.JSInterop.SetupModule("/js/local-account-recovery.js");
        module.Setup<LocalEmailConfirmationRequestDto?>("takeLocalAccountCapability").SetResult(Pointer(3));
        context.Services.GetRequiredService<BunitNavigationManager>().NavigateTo("/auth/local-account-recovery?mode=verify-email");
        var cut = context.Render<LocalAccountRecovery>();
        await Assert.That(cut.FindAll("form").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("[role=alert]").Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CurrentPasswordFormUsesOnlyCurrentUserHalAction(bool allowed)
    {
        using var context = CreateContext();
        using var handler = new CurrentUserHandler(allowed);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") };
        context.Services.AddSingleton<IUserClient>(new UserClient(http));
        var module = context.JSInterop.SetupModule("/js/local-account-recovery.js");
        module.Setup<LocalEmailConfirmationRequestDto?>("takeLocalAccountCapability").SetResult(null);
        var bff = context.JSInterop.SetupModule("/js/bff.js");
        bff.Setup<HalResourceOfUserDto>("fetchJson", "/api/User")
            .SetException(new Microsoft.JSInterop.JSException("Direct cookie-only API access is unauthorized."));
        context.Services.GetRequiredService<BunitNavigationManager>().NavigateTo("/auth/local-account-recovery?mode=change-password");
        LocalPasswordChangeRequestDto? captured = null;
        module.Setup<int>("submitLocalAccountRequest", invocation =>
        {
            if (invocation.Arguments[0] as string != "/bff/auth/local/password") return false;
            captured = invocation.Arguments[1] as LocalPasswordChangeRequestDto;
            return true;
        }).SetResult(204);
        var cut = context.Render<LocalAccountRecovery>();
        await Assert.That(cut.FindAll("form").Count).IsEqualTo(allowed ? 1 : 0);
        if (!allowed) return;
        string current = Secret(), next = Secret();
        cut.Find("#local-account-current-password").Change(current);
        cut.Find("#local-account-new-password").Change(next);
        cut.Find("#local-account-confirm-password").Change(next);
        await cut.Find("form").SubmitAsync();
        await Assert.That(captured is not null && captured.CurrentPassword == current && captured.NewPassword == next).IsTrue();
    }

    [Test]
    [Arguments("verify-email")]
    [Arguments("recover-password")]
    public async Task PublicRequestRequiresDiscoveryLinkAndReturnsNonenumeratingStatus(string mode)
    {
        using var context = CreateContext();
        var module = context.JSInterop.SetupModule("/js/local-account-recovery.js");
        module.Setup<LocalEmailConfirmationRequestDto?>("takeLocalAccountCapability").SetResult(null);
        context.JSInterop.SetupModule("/js/bff.js")
            .Setup<HalResourceOfAuthProviderConfigurationDto>("fetchJson", "/api/InstanceOnboarding/auth-provider-configuration")
            .SetResult(new HalResourceOfAuthProviderConfigurationDto
            { _links = new Dictionary<string, HalLink> { [mode] = new() { Href = "/api/auth/local/" } } });
        module.Setup<int>("submitLocalAccountRequest", _ => true).SetResult(202);
        context.Services.GetRequiredService<BunitNavigationManager>().NavigateTo("/auth/local-account-recovery?mode=" + mode);
        var cut = context.Render<LocalAccountRecovery>();
        cut.Find("#local-account-identifier").Change(Secret());
        await cut.Find("form").SubmitAsync();
        await Assert.That(cut.FindAll("[role=status]").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("input, form").Count).IsEqualTo(0);
    }
    private sealed class CurrentUserHandler(bool allowed) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get || request.RequestUri!.AbsolutePath != "/api/user")
                throw new InvalidOperationException("Unexpected current-user transport request.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new HalResourceOfUserDto
                {
                    _links = allowed
                        ? new Dictionary<string, HalLink> { ["change-password"] = new() { Href = "/api/auth/local/password" } }
                        : []
                })
            });
        }
    }
}
