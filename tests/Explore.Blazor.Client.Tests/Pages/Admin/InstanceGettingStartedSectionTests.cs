using System.Text.Json;
using Explore.Blazor.Client.Pages.Admin.Instance.Components;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class InstanceGettingStartedSectionTests
{
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

        await Assert.That(component.FindAll("a").Select(link => link.GetAttribute("href")))
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
