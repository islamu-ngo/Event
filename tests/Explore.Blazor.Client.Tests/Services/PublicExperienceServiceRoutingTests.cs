namespace Explore.Blazor.Client.Tests.Services;

public class PublicExperienceServiceRoutingTests
{
    private readonly PublicExperienceService _service = new(
        Substitute.For<IPublicExperienceClient>(),
        Substitute.For<ILogger<PublicExperienceService>>());

    [Test]
    [Arguments("LandingPage", "/home")]
    [Arguments("EventList", "/events")]
    public async Task ResolveHomeRoute_UsesGeneratedSettingsPreference(string preference, string expectedRoute)
    {
        var route = _service.ResolveHomeRoute(new PublicExperienceSettingsDto
        {
            PreferredHomePage = preference
        });

        await Assert.That(route).IsEqualTo(expectedRoute);
    }

    [Test]
    public async Task ResolveHomeRoute_ReturnsEvents_WhenSettingsAreNull()
    {
        var route = _service.ResolveHomeRoute((PublicExperienceSettingsDto?)null);

        await Assert.That(route).IsEqualTo("/events");
    }

    [Test]
    public async Task ResolveHomeRoute_ReturnsHome_WhenShellPrefersLandingPage()
    {
        var shell = new PublicExperienceShellDto
        {
            Mode = PublicExperienceMode.DiscoveryCentric,
            Home = new PublicExperienceHomeDto { PreferredHomePage = "LandingPage" }
        };

        var route = _service.ResolveHomeRoute(shell);

        await Assert.That(route).IsEqualTo("/home");
    }
}
