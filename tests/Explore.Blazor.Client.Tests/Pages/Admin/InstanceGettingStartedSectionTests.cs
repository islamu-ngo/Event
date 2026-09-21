using System.Text.Json;
using Explore.Blazor.Client.Pages.Admin.Instance.Components;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class InstanceGettingStartedSectionTests
{
    [Test]
    public async Task Journey_CategorizesServerChecksAndRetainsReasonsWithoutGrantingActions()
    {
        using var context = new BlazorTestContext();
        var onboarding = Substitute.For<IInstanceOnboardingService>();
        onboarding.GetStatusAsync().Returns(new InstanceOnboardingStatusDto { IsCompleted = true });
        onboarding.GetJourneyAsync(Arg.Any<CancellationToken>()).Returns(new HalResourceOfInstanceOnboardingJourneyDto
        {
            State = "Available",
            OperatorIdentity = new InstanceOperatorIdentityDocumentDto
            {
                PublicDisclosure = new() { IsReady = false, ReasonCodes = ["disclosure_missing"] },
                PaidCommerce = new() { IsReady = false, ReasonCodes = ["terms_missing"] }
            },
            Preflight = new OnboardingPreflightDto
            {
                WarningChecks =
                [
                    new() { Code = "dns_public_platform", RequirementCategory = "RequiredToPublish", Status = "Warning", ReasonCode = "operator_review_required", ActionRelation = "refresh" },
                    new() { Code = "smtp", RequirementCategory = "Optional", Status = "Warning", ReasonCode = "operator_review_required", ActionRelation = "refresh" }
                ]
            }
        });
        context.Services.AddSingleton(onboarding);

        var component = context.RenderMudComponent<InstanceGettingStartedSection>();

        await Assert.That(component.FindAll("[data-requirement-group]").Select(node => node.GetAttribute("data-requirement-group")))
            .IsEquivalentTo(new[] { "PublicDisclosure", "PaidCommerce", "Recommended" });
        await Assert.That(component.Find("[data-check='dns_public_platform']").Closest("[data-requirement-group]")!.GetAttribute("data-requirement-group"))
            .IsEqualTo("PublicDisclosure");
        await Assert.That(component.Find("[data-check='smtp']").GetAttribute("data-requirement-category")).IsEqualTo("Optional");
        await Assert.That(component.Find("[data-check='PublicDisclosure'] [data-reason]").TextContent).IsEqualTo("disclosure_missing");
        await Assert.That(component.Find("[data-check='PaidCommerce'] [data-reason]").TextContent).IsEqualTo("terms_missing");
        await Assert.That(component.FindAll("[data-requirement-group] a, [data-requirement-group] button")).IsEmpty();
        await Assert.That(component.FindAll("h3").Count).IsEqualTo(3);
        await Assert.That(component.FindAll("[tabindex]").Any(node => int.TryParse(node.GetAttribute("tabindex"), out var value) && value > 0)).IsFalse();
    }

    [Test]
    public async Task Refresh_AnnouncesBusyStateUntilTheExactJourneyResponse()
    {
        using var context = new BlazorTestContext();
        var onboarding = Substitute.For<IInstanceOnboardingService>();
        onboarding.GetStatusAsync().Returns(new InstanceOnboardingStatusDto { IsCompleted = true });
        var response = new TaskCompletionSource<HalResourceOfInstanceOnboardingJourneyDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        onboarding.GetJourneyAsync(Arg.Any<CancellationToken>()).Returns(response.Task);
        context.Services.AddSingleton(onboarding);
        var component = context.RenderMudComponent<InstanceGettingStartedSection>();
        await Assert.That(component.Find("section").GetAttribute("aria-busy")).IsEqualTo("true");
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        component.OnAfterRender += (_, _) => { if (component.Find("section").GetAttribute("aria-busy") == "false") rendered.TrySetResult(); };
        response.SetResult(new() { State = "Available" });
        await rendered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(component.Find("section").GetAttribute("aria-busy")).IsEqualTo("false");
        await Assert.That(component.Find("[role=status]").GetAttribute("aria-live")).IsEqualTo("polite");
    }

    [Test]
    public async Task MissingServerLinks_ExposeNoAdministrativeActions()
    {
        using var context = new BlazorTestContext();
        var onboarding = Substitute.For<IInstanceOnboardingService>();
        onboarding.GetStatusAsync().Returns(new InstanceOnboardingStatusDto { IsCompleted = true });
        context.Services.AddSingleton(onboarding);

        var component = context.RenderMudComponent<InstanceGettingStartedSection>();

        await Assert.That(component.FindAll("a").Count).IsEqualTo(0);
        await Assert.That(component.FindAll("h2").Count).IsEqualTo(1);
        await Assert.That(component.FindAll("h1").Count).IsEqualTo(0);
    }

    [Test]
    public async Task ServerLinks_ExposeProviderIdentityAndDirectoryDestinations()
    {
        using var context = new BlazorTestContext();
        var onboarding = Substitute.For<IInstanceOnboardingService>();
        onboarding.GetStatusAsync().Returns(new InstanceOnboardingStatusDto
        {
            IsCompleted = true,
            AdditionalProperties = new Dictionary<string, object>
            {
                ["_links"] = JsonSerializer.SerializeToElement(new Dictionary<string, object>
                {
                    ["manage-authentication"] = new { href = "/api/instance/auth-provider", method = "GET" },
                    ["manage-operator-identity"] = new { href = "/api/instance/operator-identity", method = "GET" },
                    ["manage-tenants"] = new { href = "/api/control-plane/tenants", method = "GET" }
                })
            }
        });
        context.Services.AddSingleton(onboarding);

        var component = context.RenderMudComponent<InstanceGettingStartedSection>();

        await Assert.That(component.FindAll("a").Select(link => link.GetAttribute("href")).Distinct())
            .IsEquivalentTo(new[] { "/settings/instance?section=auth-providers", "/settings/instance?section=operator-identity", "/admin/tenants" });
    }

    [Test]
    public async Task UnavailableStatus_HasRecoveryAndNoGuessedActions()
    {
        using var context = new BlazorTestContext();
        var onboarding = Substitute.For<IInstanceOnboardingService>();
        onboarding.GetStatusAsync().Returns((InstanceOnboardingStatusDto?)null);
        context.Services.AddSingleton(onboarding);

        var component = context.RenderMudComponent<InstanceGettingStartedSection>();

        await Assert.That(component.FindAll("[role=status]").Count).IsEqualTo(1);
        await Assert.That(component.FindAll("a").Count).IsEqualTo(0);
        await Assert.That(component.Find("button").HasAttribute("disabled")).IsFalse();
    }
}
