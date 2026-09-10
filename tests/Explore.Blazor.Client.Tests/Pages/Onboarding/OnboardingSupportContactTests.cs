// Exercises public support-contact readback through the generated branding client and existing onboarding form.
// Keeps support contact independent of Local credential email and verifies cleared server state replaces stale input.

using System.Net;
using System.Net.Http.Json;
using Bunit.TestDoubles;
using Explore.Blazor.Client.Pages.Onboarding;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Refit;

namespace Explore.Blazor.Client.Tests.Pages.Onboarding;

public sealed class OnboardingSupportContactTests
{
    [Test]
    public async Task GeneratedBrandingReadbackRestoresUpdatesAndClearsExistingSupportField()
    {
        using var context = new BlazorTestContext();
        using var transport = new BrandingTransport();
        using var client = new HttpClient(transport) { BaseAddress = new Uri("https://example.test") };
        context.Services.AddSingleton(Substitute.For<IUserService>());
        var auth = RestService.For<IBffAuthApi>(client);
        context.Services.AddSingleton<IInstanceOnboardingService>(services => new InstanceOnboardingService(
            new InstanceAuthenticationSettingsClient(client), new InstanceAuthorizationSettingsClient(client),
            new InstanceGovernanceSettingsClient(client), new InstanceMessagingSettingsClient(client),
            new InstanceOnboardingClient(client), new InstancePresentationSettingsClient(client),
            new InstanceStorageSettingsClient(client), new SystemClient(client), new TenantClient(client),
            auth, services.GetRequiredService<ILogger<InstanceOnboardingService>>(),
            services.GetRequiredService<NavigationManager>()));
        context.Services.GetRequiredService<BunitNavigationManager>().NavigateTo("/onboarding/instance");
        var cut = context.RenderMudComponent<InstanceOnboarding>();
        await cut.InvokeAsync(cut.Instance.RefreshAsync).WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(SupportInput(cut).GetAttribute("value")).IsEqualTo("support@example.test");
        transport.Contact = "changed@example.test";
        await cut.Find("button[aria-label='Refresh setup status']").ClickAsync(new MouseEventArgs())
            .WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(SupportInput(cut).GetAttribute("value")).IsEqualTo("changed@example.test");
        transport.Contact = null;
        await cut.Find("button[aria-label='Refresh setup status']").ClickAsync(new MouseEventArgs())
            .WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(SupportInput(cut).GetAttribute("value") ?? string.Empty).IsEqualTo(string.Empty);
        await Assert.That(cut.Find("input[autocomplete=username]").GetAttribute("value") ?? string.Empty).IsEqualTo(string.Empty);
    }

    private static AngleSharp.Dom.IElement SupportInput(IRenderedComponent<InstanceOnboarding> cut) =>
        cut.FindComponents<MudBlazor.MudTextField<string>>()
            .Single(field => field.Instance.For?.Body is System.Linq.Expressions.MemberExpression member
                && member.Member.Name == nameof(SelfHostOnboardingProfileDto.SupportEmail))
            .Find("input");

    private sealed class BrandingTransport : HttpMessageHandler
    {
        private readonly Guid _pendingOperationId = Guid.CreateVersion7();
        public string? Contact { get; set; } = "support@example.test";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            object value = request.RequestUri!.AbsolutePath.ToLowerInvariant() switch
            {
                "/api/instanceonboarding/status" => new
                {
                    isCompleted = false,
                    provider = "Local",
                    state = "InteractivePending",
                    isAuthenticated = true,
                    selectedDeploymentMode = "SingleTenant",
                    pendingOperationId = _pendingOperationId,
                    _links = new Dictionary<string, object>
                    {
                        ["complete-local"] = new { href = "/api/instanceonboarding/complete-local", method = "POST" }
                    }
                },
                "/api/system/onboarding-status" => new { requiresOnboarding = true, deploymentMode = "SingleTenant" },
                "/api/instance/settings/branding" => new { defaultBrandDisplayName = "Public directory", supportEmail = Contact },
                "/api/instance/settings/auth-provider/status" or "/api/instance/settings/authz-provider/status" => new { configured = true },
                "/api/system/onboarding-preflight" => new
                {
                    isReadyToLaunch = true,
                    blockingChecks = Array.Empty<object>(),
                    warningChecks = Array.Empty<object>()
                },
                _ => throw new InvalidOperationException("Unexpected request in support-contact readback.")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(value) });
        }
    }
}
